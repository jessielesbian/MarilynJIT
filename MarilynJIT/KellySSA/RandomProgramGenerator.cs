using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Linq.Expressions;
using MarilynJIT.KellySSA.Nodes;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace MarilynJIT.KellySSA
{
	public static class RandomProgramGenerator
	{
		private static Node GenerateRandomConstant(ushort index){
			Span<int> span = stackalloc int[1];
			RandomNumberGenerator.Fill(MemoryMarshal.AsBytes(span));
			return new ConstantNode(span[0] / (double)RandomNumberGenerator.GetInt32(1, int.MaxValue));
		}
		private static Node GenerateRandomAddition(ushort index){
			return new AddidionOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomSubtraction(ushort index)
		{
			return new SubtractionOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomMultiplication(ushort index)
		{
			return new MultiplyOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomDivision(ushort index)
		{
			return new DivideOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomModulo(ushort index)
		{
			return new ModuloOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomExponent(ushort index)
		{
			return new ExponentOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomLog(ushort index)
		{
			return new LogOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static Node GenerateRandomConditional(ushort index){
			return new Conditional((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
		}
		private static readonly Func<ushort, Node>[] generators = new Func<ushort, Node>[] {
			GenerateRandomConstant, GenerateRandomAddition, GenerateRandomSubtraction, GenerateRandomMultiplication, GenerateRandomDivision, GenerateRandomModulo, GenerateRandomExponent, GenerateRandomLog, GenerateRandomConditional
		};
		private static readonly int availableNodeTypes = generators.Length;
		public static Node GenerateRandomNode(ushort height){
			return generators[RandomNumberGenerator.GetInt32(0, availableNodeTypes)](height);
		}
		public static Node[] GenerateInitial(ParameterExpression[] parameterExpressions, ushort complexity){
			int pc = parameterExpressions.Length;
			if(pc >= complexity){
				throw new ArgumentException("Complexity must exceed the number of parameter expressions");
			}
			if(pc == 0){
				throw new ArgumentException("Minimum 1 input argument");
			}
			Node[] nodes = new Node[complexity];
			for(ushort i = 0; i < complexity; ++i){
				nodes[i] = i < pc ? new ArgumentNode(parameterExpressions[i]) : GenerateRandomNode(i);
			}
			return nodes;
		}
	}
}
