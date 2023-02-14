using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Profiler;
using MarilynJIT.TuringML.Nodes;
using MarilynJIT.TuringML.Transform;
using MarilynJIT.TuringML.Transform.KellySSA;

namespace MarilynJIT.TuringML
{
	public interface ITestingEnvironment{
		public double GetScore(Action<double[], double[], int> action);
	}
	public static class Trainer
	{
		private readonly struct KellySSARecombinantModificationTask{
			public readonly KellySSABasicBlock kellySSABasicBlock;
			public readonly TuringNode root;

			public KellySSARecombinantModificationTask(KellySSABasicBlock kellySSABasicBlock, TuringNode root)
			{
				this.kellySSABasicBlock = kellySSABasicBlock ?? throw new ArgumentNullException(nameof(kellySSABasicBlock));
				this.root = root ?? throw new ArgumentNullException(nameof(root));
			}
		}

		
		public static async Task<TuringNode> Train(ITestingEnvironment testingEnvironment, ushort variablesCount, ushort argumentsCount, int extendedArgumentsCount, ushort ssacomplexity, ulong iterations, int profilingRunsPerIteration, int heavilyOptimizedRunsPerIteration, ulong loopLimit, ulong coldBranchHyperoptimizationTreshold, ushort threads, int removeOutliers, TuringNode initialState){
			WorkerThread[] workerThreads = new WorkerThread[threads];
			Task[] tasks = new Task[threads];
			double[] scores = new double[threads];
			TuringNode[] bestNodes = new TuringNode[threads];
			TuringNode bestNode = initialState.DeepClone();
			RandomTransformer randomTransformer = new RandomTransformer(variablesCount, ssacomplexity, argumentsCount);

			double bestScore = double.NegativeInfinity;
			int totalRunsPerIteration = profilingRunsPerIteration + heavilyOptimizedRunsPerIteration;
			int endOfResults = totalRunsPerIteration - removeOutliers;

			double[][] buffers = new double[threads][];
			double[][] outputs = new double[threads][];
			for (ushort i = 0; i < threads; ++i){
				buffers[i] = new double[extendedArgumentsCount];
				outputs[i] = new double[variablesCount];
				workerThreads[i] = new WorkerThread();
				scores[i] = double.NegativeInfinity;
			}

			void ExecuteAgent(ushort threadid){

				double total = TestAgent(randomTransformer.Visit(randomTransformer.Visit(bestNode.DeepClone())), true, out TuringNode mine);
				if (total > scores[threadid]){
					scores[threadid] = total;
					bestNodes[threadid] = mine;
				}
			}

			double TestAgent(TuringNode mine, bool dostrip, out TuringNode newval){
				List<double> list = new List<double>(totalRunsPerIteration);

				//Initial compilation with light optimizations + profiling
				Action<double[], double[], int> func = JITCompiler.CompileProfiling(mine, variablesCount, argumentsCount, loopLimit, out LightweightBranchCounter lightweightBranchCounter);


				using(ThreadPrivateProfiler threadPrivateProfiler = ThreadPrivateProfiler.Create()){
					mine = threadPrivateProfiler.livenessProfilingCodeInjector.Visit(mine);
					for (int i = 0; i < profilingRunsPerIteration; ++i)
					{
						list.Add(testingEnvironment.GetScore(func));
					}
					mine = threadPrivateProfiler.unreachedLoopsStripper.Visit(mine);
				}
				mine = MassRemovalVisitor<ProfilingCode>.instance.Visit(mine);
				newval = mine;
				
				
				

				new UnreachedBranchesStripper(0, false, variablesCount, lightweightBranchCounter).Visit(mine);

				if (coldBranchHyperoptimizationTreshold > 0)
				{
					new UnreachedBranchesStripper(coldBranchHyperoptimizationTreshold, true, variablesCount, lightweightBranchCounter).Visit(mine);
				}

				//Recompilation with heavy optimizations + no profiling
				func = JITCompiler.Compile(mine, variablesCount, argumentsCount, loopLimit);

				//Strip optimized version
				if (coldBranchHyperoptimizationTreshold > 0 & dostrip)
				{
					KellySSAOptimizedVersionStripper.instance.Visit(mine);
				}

				for (int i = 0; i < heavilyOptimizedRunsPerIteration; ++i)
				{
					list.Add(testingEnvironment.GetScore(func));
				}

				double total = 0;

				list.Sort();
				for (int i = removeOutliers; i < endOfResults; ++i)
				{
					double score = list[i];
					if (double.IsInfinity(score) | double.IsNaN(score))
					{
						return score;
					}
					total += score;
				}
				return total;
			}
			
			Queue<KellySSABasicBlock> kellySSABasicBlocks = new Queue<KellySSABasicBlock>();
			NodeGrabber<KellySSABasicBlock> nodeGrabber = new NodeGrabber<KellySSABasicBlock>(kellySSABasicBlocks);
			ConcurrentBag<KellySSARecombinantModificationTask> kellySSARecombinantModificationTasks = new();
			Dictionary<TuringNode, KellySSABasicBlock> mapping = new(ReferenceEqualityComparer.Instance);


			void ExecuteKellySSARecombinantTrainer(){
				while(kellySSARecombinantModificationTasks.TryTake(out KellySSARecombinantModificationTask task)){
					KellySSABasicBlock kellySSABasicBlock = task.kellySSABasicBlock;
					Node[] nodes = kellySSABasicBlock.nodes;
					Transformer.RandomMutate(nodes, variablesCount);

					double score = TestAgent(task.root, false, out _);
					if (score > bestScore){
						mapping[kellySSABasicBlock].nodes = nodes;
					}
				}
			}


			for (ulong x = 0; x < iterations; ++x){
				for (ushort i = 0; i < threads; ++i)
				{
					ushort copy = i;
					tasks[i] = workerThreads[i].EnqueueWork(() => ExecuteAgent(copy));
				}
				await Task.WhenAll(tasks);
				for (ushort i = 0; i < threads; ++i)
				{
					double score = scores[i];
					//await System.IO.File.AppendAllTextAsync("c:\\users\\jessi\\desktop\\log.txt", score.ToString() + "\n");
					if (score == double.PositiveInfinity){
						return bestNodes[i];
					}
					if(score > bestScore){
						bestScore = score;
						bestNode = bestNodes[i];
					}
				}

				TuringNode mine = bestNode.DeepClone();
				nodeGrabber.Visit(mine);
				if(kellySSABasicBlocks.Count == 0){
					continue;
				}
				while (kellySSABasicBlocks.TryDequeue(out KellySSABasicBlock kellySSABasicBlock))
				{
					Dictionary<TuringNode, TuringNode> tempMapping = new(ReferenceEqualityComparer.Instance);
					TuringNode clone = mine.DeepClone(tempMapping);
					foreach (KeyValuePair<TuringNode, TuringNode> keyValuePair in tempMapping)
					{
						TuringNode key = keyValuePair.Key;
						TuringNode value = keyValuePair.Value;
						
						if (ReferenceEquals(value, kellySSABasicBlock))
						{
							mapping.Add(key, kellySSABasicBlock);
							kellySSARecombinantModificationTasks.Add(new((KellySSABasicBlock)key, clone));
							goto done;
						}
					}
					
					throw new Exception("Unable to retrace deep cloned KellySSA basic block (should not reach here)");
				done:;
				}
				if(kellySSARecombinantModificationTasks.IsEmpty){
					continue;
				}
				for (ushort i = 0; i < threads; ++i)
				{
					tasks[i] = workerThreads[i].EnqueueWork(ExecuteKellySSARecombinantTrainer);
				}
				await Task.WhenAll(tasks);
				mapping.Clear();
				double score1 = TestAgent(mine, true, out mine);
				if(score1 == double.PositiveInfinity){
					return mine;
				}
				if (score1 > bestScore)
				{
					bestScore = score1;
					bestNode = mine;
				}
			}
			return bestNode;
		}
	}
}
