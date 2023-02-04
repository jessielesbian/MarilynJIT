using MarilynJIT.KellySSA.Nodes;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarilynJIT.KellySSA.Trainer
{
	public interface IRewardFunction{
		public double GetScore(double[] inputs, double output);
	}
	public interface IDataSource{
		public void GetData(double[] buffer);
	}
	public static class Trainer{
		private sealed class Counter{
			private readonly int maxcount;
			private volatile int current;

			public Counter(int maxcount)
			{
				this.maxcount = maxcount;
			}

			public bool Increment(){
				while(true){
					int temp = current;
					if (temp == maxcount)
					{
						return false;
					}
					if(Interlocked.CompareExchange(ref current, temp + 1, temp) == temp){
						return true;
					}
				}
			}
			public void Reset(){
				current = 0;
			}
		}
		public static async Task<Node[]> Train(int profilingRunsPerIteration, int heavilyOptimizedRunsPerIteration, ulong iterations, ushort complexity, IRewardFunction rewardFunction, IDataSource dataSource, ushort argumentsCount, int threadsCount, int eliminateOutliers){

			ParameterExpression[] parameterExpressions = new ParameterExpression[argumentsCount];
			Type[] types = new Type[argumentsCount];
			for (ushort i = 0; i < argumentsCount; ++i)
			{
				types[i] = typeof(double);
				parameterExpressions[i] = Expression.Parameter(typeof(double));
			}

			Node[] nodes = RandomProgramGenerator.GenerateInitial(parameterExpressions, complexity);
			Node[] latest = new Node[complexity];
			nodes.CopyTo(latest.AsSpan());
			JITCompiler.Optimize(nodes);

			int totalRunsPerIteration = profilingRunsPerIteration + heavilyOptimizedRunsPerIteration;
			int listEndIndex = totalRunsPerIteration - eliminateOutliers;

			double bestScore = double.NegativeInfinity;
			ConcurrentBag<double> scores = new ConcurrentBag<double>();
			Task[] tasks = new Task[threadsCount];

			async Task Execute(Counter counter, Delegate method){
				for (int i = 0; i < threadsCount; ++i)
				{
					tasks[i] = Task.Run(() => {
						double[] buffer = new double[argumentsCount];
						object[] buf2 = new object[argumentsCount];
						while (counter.Increment())
						{
							dataSource.GetData(buffer);
							for (ushort i = 0; i < argumentsCount; ++i)
							{
								buf2[i] = buffer[i];
							}
							scores.Add(rewardFunction.GetScore(buffer, (double)method.DynamicInvoke(buf2)));
						}
					});
				}
				await Task.WhenAll(tasks);

			};


			Counter profilingRunsCounter = new Counter(profilingRunsPerIteration);
			Counter heavilyOptimizedRunsCounter = new Counter(heavilyOptimizedRunsPerIteration);
			
			for(ulong i = 0; i < iterations; ++i){
				
				
				//Initial compilation and execution with profiling
				using (BranchLivenessProfiler branchLivenessProfiler = new BranchLivenessProfiler())
				{
					await Execute(profilingRunsCounter, Expression.Lambda(JITCompiler.Compile(latest, branchLivenessProfiler), true, parameterExpressions).Compile());

					//Strip unreached branches
					branchLivenessProfiler.Strip(latest, argumentsCount);
				}
				profilingRunsCounter.Reset();
				JITCompiler.Optimize(latest);

				//Recompilation and execution with heavy optimizations
				await Execute(heavilyOptimizedRunsCounter, Expression.Lambda(JITCompiler.Compile(latest), true, parameterExpressions).Compile());
				heavilyOptimizedRunsCounter.Reset();

				//Sort
				List<double> list = new List<double>(totalRunsPerIteration);
				while (scores.TryTake(out double score))
				{
					list.Add(score);
				}
				list.Sort();
				double total = 0;
				for (int x = eliminateOutliers; x < listEndIndex; ++x)
				{
					double read = list[x];
					if(double.IsNaN(x) | double.IsInfinity(x)){
						total = x;
						break;
					}
					total += read;
				}

				//Infinite rewards means no more room for improvement
				if(total == double.PositiveInfinity){
					return latest;
				}
				if(total > bestScore){
					bestScore = total;
					latest.CopyTo(nodes.AsSpan());
				} else{
					nodes.CopyTo(latest.AsSpan());
				}
				RandomProgramGenerator.RandomMutate(latest, argumentsCount);
				JITCompiler.Optimize(latest);
			}
			return nodes;
		}

	}
}
