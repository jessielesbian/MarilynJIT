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
		public static async Task<Node[]> Train(int profilingRunsPerIteration, int heavilyOptimizedRunsPerIteration, ulong iterations, ushort complexity, IRewardFunction rewardFunction, IDataSource dataSource, ushort argumentsCount, ushort threadsCount, int eliminateOutliers){

			ParameterExpression[] parameterExpressions = new ParameterExpression[argumentsCount];
			Type[] types = new Type[argumentsCount];
			for (ushort i = 0; i < argumentsCount; ++i)
			{
				types[i] = typeof(double);
				parameterExpressions[i] = Expression.Parameter(typeof(double));
			}

			Node[] nodes = RandomProgramGenerator.GenerateInitial(parameterExpressions, complexity);
			JITCompiler.Optimize(nodes);

			int totalRunsPerIteration = profilingRunsPerIteration + heavilyOptimizedRunsPerIteration;
			int listEndIndex = totalRunsPerIteration - eliminateOutliers;

			double bestScore = double.NegativeInfinity;

			double[] bestScores = new double[threadsCount];
			
			WorkerThread[] workerThreads = new WorkerThread[threadsCount];
			Node[][] latestMaps = new Node[threadsCount][];
			for (ushort i = 0; i < threadsCount; ++i){
				workerThreads[i] = new WorkerThread();
				latestMaps[i] = new Node[complexity];
			}
			
			Task[] tasks = new Task[threadsCount];
			

			void ExecuteGenerativeAdverserialAgent(ushort id){
				Node[] latest = latestMaps[id];
				nodes.CopyTo(latest.AsSpan());
				Queue<double> scores = new Queue<double>();
				Queue<ushort> reRandomizeQueue = new Queue<ushort>();
				void Execute(int iterations, Expression expression)
				{
					Delegate method = Expression.Lambda(expression, true, parameterExpressions).Compile(false);
					double[] buffer = new double[argumentsCount];
					object[] buf2 = new object[argumentsCount];
					for(int z = 0; z < iterations; ++z)
					{
						dataSource.GetData(buffer);
						for (ushort i = 0; i < argumentsCount; ++i)
						{
							buf2[i] = buffer[i];
						}
						object temp;

						try
						{
							temp = method.DynamicInvoke(buf2);
						}
						catch (TargetInvocationException e)
						{
							if (e.InnerException is DynamicInvalidOperation dynamicInvalidOperation)
							{
								return;
							}
							throw;
						}

						scores.Enqueue(rewardFunction.GetScore(buffer, (double)temp));
					}


				};

				RandomProgramGenerator.RandomMutate(latest, argumentsCount);
				RandomProgramGenerator.StripStaticInvalidValues(latest);
				JITCompiler.Optimize(latest);

				//Initial compilation and execution with branch liveness profiling and dynamic invalid operation elimination
				while (true)
				{
					using (BranchLivenessProfiler branchLivenessProfiler = new BranchLivenessProfiler())
					{
						Execute(profilingRunsPerIteration, JITCompiler.Compile(latest, true, branchLivenessProfiler));

						//Strip unreached branches
						branchLivenessProfiler.Strip(latest, argumentsCount);
					}
					if (reRandomizeQueue.Count == 0)
					{
						break;
					}
					else
					{
						RandomProgramGenerator.RandomizeImpl(latest, reRandomizeQueue);
						RandomProgramGenerator.StripStaticInvalidValues(latest);
						JITCompiler.Optimize(latest);
						scores.Clear();
					}
				}

				//Recompilation and execution with heavy optimizations
				Execute(heavilyOptimizedRunsPerIteration, JITCompiler.Compile(latest));

				//Sort
				List<double> list = new List<double>(totalRunsPerIteration);
				while (scores.TryDequeue(out double score))
				{
					list.Add(double.IsNaN(score) ? double.NegativeInfinity : score);
				}
				list.Sort();
				double total = 0;
				for (int x = eliminateOutliers; x < listEndIndex; ++x)
				{
					double read = list[x];

					if (read == double.PositiveInfinity)
					{
						bestScores[id] = double.PositiveInfinity;
						return;
					}

					if (read == double.NegativeInfinity)
					{
						bestScores[id] = double.NegativeInfinity;
						return;
					}
					total += read;
				}
				bestScores[id] = total;
			}
			int skipped = -1;
			for(ulong z = 0; z < iterations; ++z){
				for (ushort i = 0; i < threadsCount; ++i)
				{
					if(skipped != i){
						nodes.CopyTo(latestMaps[i].AsSpan());
					}
					ushort temp = i;
					tasks[i] =  workerThreads[i].EnqueueWork(() => ExecuteGenerativeAdverserialAgent(temp));
				}
				await Task.WhenAll(tasks);

				Node[] copyfrom = null;
				for (ushort i = 0; i < threadsCount; ++i)
				{
					double rewards = bestScores[i];
					if (rewards == double.PositiveInfinity){
						return latestMaps[i];
					}
					if(rewards > bestScore){
						bestScore = rewards;
						copyfrom = latestMaps[i];
						skipped = i;
					}
				}
				if(copyfrom is null){
					skipped = -1;
					continue;
				}
				copyfrom.CopyTo(nodes.AsSpan());
			}

			return nodes;
		}

	}
}
