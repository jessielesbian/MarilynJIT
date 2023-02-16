using MarilynJIT.KellySSA.Nodes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace MarilynJIT.KellySSA
{
	public sealed class OptimizedBailout : Exception{
		
	}
	public static class JITCompiler
	{
		public static void PruneUnreached(Node[] nodes, ushort offset, IReadOnlyDictionary<Conditional, bool> taken, IReadOnlyDictionary<Conditional, bool> notTaken, bool emitBailout){
			if(offset < 1){
				throw new ArgumentOutOfRangeException(nameof(offset));
			}
			int count = nodes.Length;
			for(ushort i = offset; i < count; ++i){
				Node node = nodes[i];
				if(node is null){
					continue;
				}
				if(node is Conditional conditional){
					bool nodeTaken = taken.ContainsKey(conditional);
					if(nodeTaken & notTaken.ContainsKey(conditional))
					{
						continue;
					}
					nodes[i] = conditional.If(nodes.AsSpan(0, i), nodeTaken, emitBailout);
				}
			}
		}
		public static void Optimize(Node[] nodes, ushort removalProtectedRegionStart){
			ushort count = (ushort)nodes.Length;
			if(count < 2){
				return;
			}
		start:
			bool optimized = false;
			for(ushort i = 1; i < count; ++i){
				Node old = nodes[i];
				if (old is null){
					continue;
				}
				Node optimizedNode = old.Optimize(nodes.AsSpan(0, i));
				if(ReferenceEquals(old, optimizedNode)){
					continue;
				}
				nodes[i] = optimizedNode;
				optimized = true;
			}
			Dictionary<int, bool> usefuls = new();
			for (int i = count - 1; i > 0; i--)
			{
				
				if(i < removalProtectedRegionStart)
				{
					if(!usefuls.ContainsKey(i)){
						Node node1 = nodes[i];
						if (!(node1 is null | node1 is IRemovalProtectedNode)){
							optimized = true;
							nodes[i] = null;
							continue;
						}
					}

				}
				Node node = nodes[i];
				if (node is null){
					continue;
				}
				foreach (ushort read in node.GetReads()){
					usefuls.TryAdd(read, false);
				}	
			}
			if(optimized){
				goto start;
			}
		}
		public static Action<double[], double[]> Compile(Node[] nodes, ushort outputsCount, bool checking, IProfilingCodeGenerator profilingCodeGenerator = null){
			ushort length = (ushort)nodes.Length;
			
			bool[] noinline = new bool[length];
			for(ushort i = 0; i < length; ++i){
				Node node = nodes[i];
				if(node is null | node is IDirectCompileNode)
				{
					continue;
				}

				bool touched = false;
				for(int x = i + 1; x < length; ++x){
					Node temp = nodes[x];
					if(temp is null){
						continue;
					}
					foreach (ushort read in temp.GetReads()){
						if(read == i){
							if(touched){
								noinline[i] = true;
								goto livenesscheckcomplete;
							}
							touched = true;
							continue;
						}
					}
				}
			livenesscheckcomplete:;
			}
			Expression[] expressions = new Expression[length];
			List<Expression> list = new List<Expression>();
			List<ParameterExpression> variables = new List<ParameterExpression>();
			ParameterExpression profilerParameter;
			ParameterExpression profilerVariable;
			if (profilingCodeGenerator is null) {
				profilerParameter = null;
				profilerVariable = null;
			} else{
				Type profilerType = profilingCodeGenerator.GetType();
				profilerVariable = Expression.Variable(profilerType);
				variables.Add(profilerVariable);
				profilerParameter = Expression.Parameter(typeof(object));
				list.Add(Expression.Assign(profilerVariable, Expression.TypeAs(profilerVariable, profilerType)));
			}
			ParameterExpression inputArray = Expression.Parameter(typeof(double[]));
			for (ushort i = 0; i < length; ++i)
			{
				Node node = nodes[i];
				if(node is null){
					continue;
				}
				Expression compiled;

				if(node is Conditional conditional){
					if(profilingCodeGenerator is { }){
						profilingCodeGenerator.Generate(conditional, profilerVariable, out Expression taken, out Expression notTaken);
						compiled = conditional.CompileProfiling(expressions.AsSpan(0, i), taken, notTaken);
						goto donecompile;
					}
				}
				if(checking){
					if (node is ICheckedOperator checkedOperator)
					{
						compiled = checkedOperator.CompileWithChecking(expressions.AsSpan(0, i), i);
						goto donecompile;
					}
				}
				compiled = node.Compile(expressions.AsSpan(0, i), inputArray);
			donecompile:

				if (noinline[i]){
					ParameterExpression variable = Expression.Variable(typeof(double));
					variables.Add(variable);
					list.Add(Expression.Assign(variable, compiled));
					compiled = variable;
				}
				expressions[i] = compiled;
			}
			ParameterExpression outputArray = Expression.Parameter(typeof(double[]));
			for (int i = length - outputsCount; i < length; ++i){
				list.Add(Expression.Assign(Expression.ArrayAccess(outputArray, Expression.Constant(i, typeof(int))), expressions[i]));
			}
			Expression totalCompile = Expression.Block(variables, list);
			if (profilingCodeGenerator is null){
				return Expression.Lambda<Action<double[], double[]>>(totalCompile, true, inputArray, outputArray).Compile(false);
			}
			Action<double[], double[], object> inner = Expression.Lambda<Action<double[], double[], object>>(totalCompile, true, inputArray, outputArray, profilerParameter).Compile(false);

			return (double[] input, double[] output) => inner(input, output, profilingCodeGenerator);
		}
	}
	public interface IProfilingCodeGenerator{
		public void Generate(Conditional conditional, Expression theProfiler, out Expression taken, out Expression notTaken);
	}
}
