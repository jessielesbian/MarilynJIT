using NUnit.Framework;
using System.Linq.Expressions;
using System;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Trainer;
using MarilynJIT.KellySSA.Profiler;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;

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
				if(double.IsNaN(output) | double.IsInfinity(output)){
					return double.NegativeInfinity;
				}
				double temp = Math.Abs((inputs[0] + inputs[1]) - output);
				if(temp == 0){
					return double.PositiveInfinity;
				}
				return 0 - temp;
			}
		}
		[Test]
		public void KellySSAVirtualMachine(){
			ParameterExpression[] parameterExpressions = new ParameterExpression[] { Expression.Parameter(typeof(double)) };
			Node[] nodes = RandomProgramGenerator.GenerateInitial(parameterExpressions, 256);
			double unoptimized = Expression.Lambda<Func<double, double>>(JITCompiler.Compile(nodes, parameterExpressions), parameterExpressions).Compile()(0);
			JITCompiler.Optimize(nodes);
			using (BranchCounter branchCounter = new BranchCounter()){
				Assert.AreEqual(unoptimized, Expression.Lambda<Func<double, double>>(JITCompiler.Compile(nodes, parameterExpressions, false, branchCounter), parameterExpressions).Compile()(0));
				branchCounter.Strip(nodes, 1, 0, false);
			}
			JITCompiler.Optimize(nodes);
			Assert.AreEqual(unoptimized, Expression.Lambda<Func<double, double>>(JITCompiler.Compile(nodes, parameterExpressions), parameterExpressions).Compile()(0));
		}

		[Test]
		public async Task KellySSATraining(){
			Node[] nodes = await Trainer.Train(16, 256, ulong.MaxValue, 256, new AdditionTrainingRewardFunction(), new AdditionTrainingDataSource(), 2, 8, 17, 5);

			Serialization.DeserializeJsonNodesArray(JsonConvert.SerializeObject(nodes));
		}
	}
}