using MarilynJIT.KellySSA;
using MarilynJIT.TuringML.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using MarilynJIT.KellySSA.Profiler;

namespace MarilynJIT.TuringML
{
	public sealed class AIBailoutException : Exception{
		
	}
	public static class JITCompiler
	{
		private static readonly Expression zero = Expression.Constant((ulong)0, typeof(ulong));
		private static readonly Expression fzero = Expression.Constant(0.0, typeof(double));
		private static readonly Expression bailout = Expression.Throw(Expression.New(typeof(AIBailoutException)));
		private static readonly Expression createMemoryArray = Expression.NewArrayBounds(typeof(double), Expression.Constant(65536));
		private static IEnumerable<ParameterExpression> Combine(IEnumerable<ParameterExpression> variables, ParameterExpression parameterExpression){
			foreach(ParameterExpression parameterExpression1 in variables){
				yield return parameterExpression1;
			}
			yield return parameterExpression;
		}
		private static Expression CompileImpl(TuringNode turingNode, ushort variablesCount, ushort argumentsCount, ulong loopLimit, LightweightBranchCounter lightweightBranchCounter, ParameterExpression array, ParameterExpression outputArray)
		{
			ParameterExpression counter = Expression.Parameter(typeof(ulong));
			ParameterExpression memoryArray = Expression.Variable(typeof(double[]));

			Expression safepoint = Expression.IfThen(Expression.GreaterThan(Expression.PreIncrementAssign(counter), Expression.Constant(loopLimit)), bailout);

			ParameterExpression[] variables = new ParameterExpression[variablesCount];
			List<Expression> expressions = new List<Expression>((variablesCount + 2) * 2);
			expressions.Add(Expression.Assign(counter, zero));
			expressions.Add(Expression.Assign(memoryArray, createMemoryArray));
			for (int i = 0; i < variablesCount; ++i)
			{
				ParameterExpression variable = Expression.Variable(typeof(double));
				expressions.Add(Expression.Assign(variable, i < argumentsCount ? Expression.ArrayAccess(array, Expression.Constant(i, typeof(int))) : fzero));
				variables[i] = variable;
			}
			expressions.Add(turingNode.Compile(variables, safepoint, memoryArray, lightweightBranchCounter));
			expressions.Add(variables[variablesCount - 1]);
			for (int i = 0; i < variablesCount; ++i)
			{
				expressions.Add(Expression.Assign(Expression.ArrayAccess(outputArray, Expression.Constant(i, typeof(int))), variables[i]));
			}
			return Expression.Block(Combine(Combine(variables, counter), memoryArray), expressions);
		}
		public static Action<double[], double[]> CompileProfiling(TuringNode turingNode, ushort variablesCount, ushort argumentsCount, ulong loopLimit, out LightweightBranchCounter lightweightBranchCounter){
			ParameterExpression array = Expression.Parameter(typeof(double[]));
			ParameterExpression getProfiler = Expression.Parameter(typeof(LightweightBranchCounter));
			LightweightBranchCounter lightweightBranchCounter1 = new LightweightBranchCounter(getProfiler);
			lightweightBranchCounter = lightweightBranchCounter1;
			ParameterExpression outputArray = Expression.Parameter(typeof(double[]));

			Action<double[], LightweightBranchCounter, double[]> action = Expression.Lambda<Action<double[], LightweightBranchCounter, double[]>>(CompileImpl(turingNode, variablesCount, argumentsCount, loopLimit, lightweightBranchCounter1, array, outputArray), true, array, getProfiler, outputArray).Compile(false);

			return (double[] input, double[] output) => action(input, lightweightBranchCounter1, output);
		}
		public static Action<double[], double[]> Compile(TuringNode turingNode, ushort variablesCount, ushort argumentsCount, ulong loopLimit)
		{
			ParameterExpression array = Expression.Parameter(typeof(double[]));
			ParameterExpression outputArray = Expression.Parameter(typeof(double[]));
			return Expression.Lambda<Action<double[], double[]>>(CompileImpl(turingNode, variablesCount, argumentsCount, loopLimit, null, array, outputArray), true, array, outputArray).Compile(false);
		}
	}
}
