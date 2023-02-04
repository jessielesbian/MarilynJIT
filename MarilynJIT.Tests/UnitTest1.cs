using NUnit.Framework;
using System.Linq.Expressions;
using System;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Trainer;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace MarilynJIT.Tests
{
	public sealed class Tests
	{

		[SetUp]
		public void Setup()
		{
		}
		private sealed class AdditionTrainingDataSource : IDataSource
		{
			public void GetData(double[] buffer)
			{
				Span<long> span = stackalloc long[2];
				RandomNumberGenerator.Fill(MemoryMarshal.AsBytes(span));
				buffer[0] = span[0] / 1024.0;
				buffer[1] = span[1] / 1024.0;
			}
		}
		private sealed class AdditionTrainingRewardFunction : IRewardFunction
		{
			public double GetScore(double[] inputs, double output)
			{
				return 0 - Math.Abs((inputs[0] + inputs[1]) - output);
			}
		}
		[Test]
		public void KellySSAVirtualMachine(){
			ParameterExpression[] parameterExpressions = new ParameterExpression[] { Expression.Parameter(typeof(double)) };
			Node[] nodes = RandomProgramGenerator.GenerateInitial(parameterExpressions, 256);
			double unoptimized = Expression.Lambda<Func<double, double>>(JITCompiler.Compile(nodes), parameterExpressions).Compile()(0);
			JITCompiler.Optimize(nodes);
			using (BranchLivenessProfiler branchLivenessProfiler = new BranchLivenessProfiler()){
				Assert.AreEqual(unoptimized, Expression.Lambda<Func<double, double>>(JITCompiler.Compile(nodes, branchLivenessProfiler), parameterExpressions).Compile()(0));
				branchLivenessProfiler.Strip(nodes, 1);
			}
			JITCompiler.Optimize(nodes);
			Assert.AreEqual(unoptimized, Expression.Lambda<Func<double, double>>(JITCompiler.Compile(nodes), parameterExpressions).Compile()(0));
		}

		[Test]
		public async Task KellySSATraining(){
			await Trainer.Train(256, 4096, 256, 65535, new AdditionTrainingRewardFunction(), new AdditionTrainingDataSource(), 2, 2, 17);
		}
	}
}