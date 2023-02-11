using MarilynJIT.KellySSA;
using MarilynJIT.TuringML.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using MarilynJIT.KellySSA.Profiler;
using MarilynJIT.KellySSA;

namespace MarilynJIT.TuringML
{
	public sealed class AIBailoutException : Exception{
		
	}
	public static class JITCompiler
	{
		private static readonly Expression zero = Expression.Constant((ulong)0, typeof(ulong));
		private static readonly Expression fzero = Expression.Constant(0.0, typeof(double));
		private static readonly Expression bailout = Expression.Throw(Expression.New(typeof(AIBailoutException)));
		private static IEnumerable<ParameterExpression> Combine(ParameterExpression[] variables, ParameterExpression parameterExpression){
			foreach(ParameterExpression parameterExpression1 in variables){
				yield return parameterExpression1;
			}
			yield return parameterExpression;
		}
		private static Expression CompileImpl(TuringNode turingNode, ushort variablesCount, ushort argumentsCount, ulong loopLimit, LightweightBranchCounter lightweightBranchCounter, ParameterExpression array)
		{
			ParameterExpression counter = Expression.Parameter(typeof(ulong));
			Expression safepoint = Expression.IfThen(Expression.GreaterThan(Expression.PreIncrementAssign(counter), Expression.Constant(loopLimit)), bailout);

			ParameterExpression[] variables = new ParameterExpression[variablesCount];
			List<Expression> expressions = new List<Expression>(variablesCount + 3);
			expressions.Add(Expression.Assign(counter, zero));
			for (int i = 0; i < variablesCount; ++i)
			{
				ParameterExpression variable = Expression.Variable(typeof(double));
				expressions.Add(Expression.Assign(variable, i < argumentsCount ? Expression.ArrayAccess(array, Expression.Constant(i, typeof(int))) : fzero));
				variables[i] = variable;
			}
			expressions.Add(turingNode.Compile(variables, safepoint, lightweightBranchCounter));
			expressions.Add(variables[variablesCount - 1]);
			return Expression.Block(typeof(double), Combine(variables, counter), expressions);
		}
		public static Func<double[], double> CompileProfiling(TuringNode turingNode, ushort variablesCount, ushort argumentsCount, ulong loopLimit, out LightweightBranchCounter lightweightBranchCounter){
			ParameterExpression array = Expression.Parameter(typeof(double[]));
			ParameterExpression getProfiler = Expression.Parameter(typeof(LightweightBranchCounter));
			LightweightBranchCounter lightweightBranchCounter1 = new LightweightBranchCounter(getProfiler);
			lightweightBranchCounter = lightweightBranchCounter1;
			return (double[] input) => Expression.Lambda<Func<double[], LightweightBranchCounter, double>>(CompileImpl(turingNode, variablesCount, argumentsCount, loopLimit, lightweightBranchCounter1, array), true, array, getProfiler).Compile(false)(input, lightweightBranchCounter1);
		}
		public static Func<double[], double> Compile(TuringNode turingNode, ushort variablesCount, ushort argumentsCount, ulong loopLimit)
		{
			ParameterExpression array = Expression.Parameter(typeof(double[]));
			return Expression.Lambda<Func<double[], double>>(CompileImpl(turingNode, variablesCount, argumentsCount, loopLimit, null, array), true, array).Compile(false);
		}
		public static Func<double[], double> PostfixWithRewardFunction(Func<double[], double> func, IRewardFunction rewardFunction){
			return (double[] array) => {
				try
				{
					return rewardFunction.GetScore(array, func(array));
				}
				catch (AIBailoutException)
				{
					return double.NegativeInfinity;
  				}
			};
		}
	}
}
