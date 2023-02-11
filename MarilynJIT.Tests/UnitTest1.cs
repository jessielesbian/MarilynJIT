using NUnit.Framework;
using System.Linq.Expressions;
using System;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Profiler;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json;
using MarilynJIT.TuringML.Nodes;
using MarilynJIT.TuringML.Transform;
using MarilynJIT.TuringML;

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
		private sealed class AdditionTrainingRewardFunction : KellySSA.IRewardFunction
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
		private sealed class AdditionTrainingTuringMLRewardFunction : TuringML.IRewardFunction
		{
			public double GetScore(double[] inputs, double[] output1)
			{
				double output = output1[output1.Length - 1];
				if (double.IsNaN(output) | double.IsInfinity(output))
				{
					return double.NegativeInfinity;
				}
				double temp = Math.Abs(inputs[0] + inputs[1] - output);
				if (temp == 0)
				{
					return double.PositiveInfinity;
				}
				return 0 - temp;
			}
		}
		[Test]
		public void KellySSAVirtualMachine(){
			ParameterExpression[] parameterExpressions = new ParameterExpression[] { Expression.Parameter(typeof(double)) };
			Node[] nodes = RandomProgramGenerator.GenerateInitial(1, 256);
			double unoptimized = Expression.Lambda<Func<double, double>>(KellySSA.JITCompiler.Compile(nodes, parameterExpressions), parameterExpressions).Compile()(0);
			KellySSA.JITCompiler.Optimize(nodes);
			using (BranchCounter branchCounter = new BranchCounter()){
				Assert.AreEqual(unoptimized, Expression.Lambda<Func<double, double>>(KellySSA.JITCompiler.Compile(nodes, parameterExpressions, false, branchCounter), parameterExpressions).Compile()(0));
				branchCounter.Strip(nodes, 1, 0, false);
			}
			KellySSA.JITCompiler.Optimize(nodes);
			Assert.AreEqual(unoptimized, Expression.Lambda<Func<double, double>>(KellySSA.JITCompiler.Compile(nodes, parameterExpressions), parameterExpressions).Compile()(0));
		}

		[Test]
		public async Task KellySSATraining(){
			Node[] nodes = await KellySSA.Trainer.Train(16, 256, ulong.MaxValue, 256, new AdditionTrainingRewardFunction(), new AdditionTrainingDataSource(), 256, 8, 17, 5);

			KellySSA.Nodes.Serialization.DeserializeJsonNodesArray(JsonConvert.SerializeObject(nodes));
		}

		[Test]
		public void TuringMLJITCompiler(){
			RandomTransformer randomTransformer = new RandomTransformer(16, 256);
			TuringNode turingNode = new Block();
			ParameterExpression[] variables = new ParameterExpression[16];
			for (byte i = 0; i < 16; ++i)
			{
				variables[i] = Expression.Parameter(typeof(double));
			}
			for (byte i = 0; i < 255; ++i){
				turingNode = randomTransformer.Visit(turingNode);
			}
			turingNode.Compile(variables, Expression.Block(), Expression.Variable(typeof(double[])), null);	
		}

		[Test]
		public async Task TuringMLTraining(){
			await TuringML.Trainer.Train(new AdditionTrainingDataSource(), new AdditionTrainingTuringMLRewardFunction(), 16, 2, 2, 256, 2, 256, 256, 256, 4, 16, 16);
		}
	}
}