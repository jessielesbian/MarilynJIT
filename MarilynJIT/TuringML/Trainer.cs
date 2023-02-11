using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using MarilynJIT.KellySSA.Profiler;
using MarilynJIT.TuringML.Nodes;
using MarilynJIT.TuringML.Transform;
using MarilynJIT.TuringML.Transform.KellySSA;
using MarilynJIT.KellySSA;

namespace MarilynJIT.TuringML
{
	public static class Trainer
	{
		public static async Task<TuringNode> Train(IDataSource dataSource, IRewardFunction rewardFunction, ushort variablesCount, ushort argumentsCount, int extendedArgumentsCount, ushort ssacomplexity, ulong iterations, int profilingRunsPerIteration, int heavilyOptimizedRunsPerIteration, ulong loopLimit, ulong coldBranchHyperoptimizationTreshold, ushort threads, int removeOutliers){
			WorkerThread[] workerThreads = new WorkerThread[threads];
			Task[] tasks = new Task[threads];
			double[] scores = new double[threads];
			TuringNode[] bestNodes = new TuringNode[threads];
			TuringNode bestNode = new Block();
			RandomTransformer randomTransformer = new RandomTransformer(variablesCount, ssacomplexity);

			double bestScore = double.NegativeInfinity;
			int totalRunsPerIteration = profilingRunsPerIteration + heavilyOptimizedRunsPerIteration;
			int endOfResults = totalRunsPerIteration - removeOutliers;

			double[][] buffers = new double[threads][];
			for(ushort i = 0; i < threads; ++i){
				buffers[i] = new double[extendedArgumentsCount];
				workerThreads[i] = new WorkerThread();
				scores[i] = double.NegativeInfinity;
			}

			void ExecuteAgent(ushort threadid){
				TuringNode mine = randomTransformer.Visit(bestNode.DeepClone());
				List<double> list = new List<double>(totalRunsPerIteration);

				//Initial compilation with light optimizations + profiling
				Func<double[], double> func = JITCompiler.PostfixWithRewardFunction(JITCompiler.CompileProfiling(mine, variablesCount, argumentsCount, loopLimit, out LightweightBranchCounter lightweightBranchCounter), rewardFunction);
				double[] buffer = buffers[threadid];
				for (int i = 0; i < profilingRunsPerIteration; ++i){
					dataSource.GetData(buffer);
					list.Add(func(buffer));
				}

				new UnreachedBranchesStripper(0, false, variablesCount, lightweightBranchCounter).Visit(mine);

				//Recompilation with heavy optimizations + no profiling
				func = JITCompiler.PostfixWithRewardFunction(JITCompiler.Compile(coldBranchHyperoptimizationTreshold == 0 ? mine : new UnreachedBranchesStripper(coldBranchHyperoptimizationTreshold, true, variablesCount, lightweightBranchCounter).Visit(mine.DeepClone()), variablesCount, argumentsCount, loopLimit), rewardFunction);

				for (int i = 0; i < heavilyOptimizedRunsPerIteration; ++i)
				{
					dataSource.GetData(buffer);
					list.Add(func(buffer));
				}

				double total = 0;

				list.Sort();
				for (int i = removeOutliers; i < endOfResults; ++i)
				{
					double score = list[i];
					if(double.IsInfinity(score)){
						total = score;
						break;
					}
					total += score;
				}

				if (total > scores[threadid]){
					scores[threadid] = total;
					bestNodes[threadid] = mine;
				}
			}

			for(ulong x = 0; x < iterations; ++x){
				for (ushort i = 0; i < threads; ++i)
				{
					ushort copy = i;
					tasks[i] = workerThreads[i].EnqueueWork(() => ExecuteAgent(copy));
				}
				await Task.WhenAll(tasks);
				for (ushort i = 0; i < threads; ++i)
				{
					double score = scores[i];
					if(score == double.PositiveInfinity){
						return bestNodes[i];
					}
					if(score > bestScore){
						bestScore = score;
						bestNode = bestNodes[i];
					}
				}
			}
			return bestNode;
		}
	}
}
