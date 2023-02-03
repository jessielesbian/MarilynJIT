using NUnit.Framework;
using System.Linq.Expressions;
using System;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;

namespace MarilynJIT.Tests
{
	public sealed class Tests
	{

		[SetUp]
		public void Setup()
		{
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
	}
}