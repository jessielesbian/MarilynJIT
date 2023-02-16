using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Profiler;
using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;

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
			Node[] nodes = RandomProgramGenerator.GenerateInitial(1, 256);
			JITCompiler.Optimize(nodes, 255);
			JITCompiler.Compile(nodes, 1, false)(new double[1], new double[1]);
		}
	}
}