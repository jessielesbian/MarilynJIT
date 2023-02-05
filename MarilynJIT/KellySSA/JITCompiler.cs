using MarilynJIT.KellySSA.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace MarilynJIT.KellySSA
{
	public static class JITCompiler
	{
		public static void PruneUnreached(Node[] nodes, ushort offset, IReadOnlyDictionary<Conditional, bool> taken, IReadOnlyDictionary<Conditional, bool> notTaken){
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
					nodes[i] = conditional.If(nodes.AsSpan(0, i), nodeTaken);
				}
			}
		}
		public static void Optimize(Node[] nodes){
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
			Dictionary<ushort, bool> usefuls = new();
			for (ushort i = count; i > 0; )
			{
				
				if(i-- < count){
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
		public static Expression Compile(Node[] nodes, bool trapDynamicInvalidOperations = false, IProfilingCodeGenerator profilingCodeGenerator = null)
		{
			ushort length = (ushort)nodes.Length;
			Expression[] expressions = new Expression[length];
			List<Expression> expressions1 = new List<Expression>(length + 1);
			List<ParameterExpression> variables = new List<ParameterExpression>();
			Node first = nodes[0];
			if(first is { }){
				if (first is IDirectCompileNode)
				{
					expressions[0] = first.Compile(ReadOnlySpan<Expression>.Empty);
				} else{
					throw new ArgumentException("The first node must be directy compilable");
				}
			}
	
			for(ushort i = 1; i < length; ++i){
				Node node = nodes[i];
				if(node is null){
					continue;
				}
				ReadOnlySpan<Expression> prev = expressions.AsSpan(0, i);
				Expression compiled;
				if(trapDynamicInvalidOperations){
					if(node is ICheckedOperator checkedOperator){
						compiled = checkedOperator.CompileWithChecking(prev, i);
						goto doneCompile;
					}
				}
				if(profilingCodeGenerator is { }){
					if(node is Conditional conditional)
					{
						profilingCodeGenerator.Generate(conditional, out Expression taken, out Expression notTaken);
						compiled = conditional.CompileProfiling(prev, taken, notTaken);
						goto doneCompile;
					}
				}

				compiled = node.Compile(prev);
				if (node is IDirectCompileNode)
				{
					expressions[i] = compiled;
					continue;
				}
			doneCompile:
				ParameterExpression variable = Expression.Variable(typeof(double));
				expressions1.Add(Expression.Assign(variable, compiled));
				expressions[i] = variable;
				variables.Add(variable);
			}
			expressions1.Add(expressions[length - 1] ?? throw new Exception("should not reach here"));
			return Expression.Block(variables, expressions1);
		}
	}
	public interface IProfilingCodeGenerator{
		public void Generate(Conditional conditional, out Expression taken, out Expression notTaken);
	}
}
