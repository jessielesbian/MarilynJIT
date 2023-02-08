using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Profiler;
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
		public static async Task<Node[]> Train(int profilingRunsPerIteration, int heavilyOptimizedRunsPerIteration, ulong iterations, ushort complexity, IRewardFunction rewardFunction, IDataSource dataSource, ushort argumentsCount, ushort threadsCount, int eliminateOutliers, ulong edgeCaseHyperoptimizationTreshold){

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
			ConcurrentBag<Node[]> hyperoptimizedPool = new ConcurrentBag<Node[]>();
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
				void Execute(int iterations, Expression expression, Expression deoptimizedExpression)
				{
					Delegate method = Expression.Lambda(expression, true, parameterExpressions).Compile(false);
					Delegate deoptimizedMethod = null;
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

						Delegate current = method;
					deoptimized:
						try
						{
							temp = current.DynamicInvoke(buf2);
						}
						catch (TargetInvocationException e)
						{
							Exception inner = e.InnerException;
							if (inner is DynamicInvalidOperation dynamicInvalidOperation)
							{
								reRandomizeQueue.Enqueue(dynamicInvalidOperation.offset);
								return;
							}
							if(inner is OptimizedBailout){
								if (deoptimizedMethod is null){
									deoptimizedMethod = Expression.Lambda(deoptimizedExpression, true, parameterExpressions).Compile(false);
								}
								current = deoptimizedMethod;
								goto deoptimized;
							}
							throw;
						}

						scores.Enqueue(rewardFunction.GetScore(buffer, (double)temp));
					}


				};

				RandomProgramGenerator.RandomMutate(latest, argumentsCount);
				RandomProgramGenerator.StripStaticInvalidValues(latest);
				JITCompiler.Optimize(latest);

				Node[] hyperoptimized = null;

				//Initial compilation and execution with branch liveness profiling and dynamic invalid operation elimination
				while (true)
				{
					using (BranchCounter branchCounter = new BranchCounter())
					{
						Execute(profilingRunsPerIteration, JITCompiler.Compile(latest, parameterExpressions, true, branchCounter), null);

						//Strip unreached branches
						branchCounter.Strip(latest, argumentsCount, 0, false);

						//Generate hyperoptimized code
						if(edgeCaseHyperoptimizationTreshold == 0){
							continue;
						}
						if (hyperoptimized is null)
						{
							if (!hyperoptimizedPool.TryTake(out hyperoptimized))
							{
								hyperoptimized = new Node[complexity];
							}
						}
						latest.CopyTo(hyperoptimized.AsSpan());
						branchCounter.Strip(hyperoptimized, argumentsCount, edgeCaseHyperoptimizationTreshold, true);
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

				JITCompiler.Optimize(hyperoptimized);
				Expression hyperoptimizedExpression = JITCompiler.Compile(hyperoptimized, parameterExpressions);
				hyperoptimizedPool.Add(hyperoptimized);


				//Recompilation and execution with heavy optimizations
				Execute(heavilyOptimizedRunsPerIteration, hyperoptimizedExpression, JITCompiler.Compile(latest, parameterExpressions));

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
