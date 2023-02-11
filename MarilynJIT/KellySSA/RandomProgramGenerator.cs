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
			return new AdditionOperator((ushort)RandomNumberGenerator.GetInt32(0, index), (ushort)RandomNumberGenerator.GetInt32(0, index));
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
			GenerateRandomConstant, GenerateRandomAddition, GenerateRandomSubtraction, GenerateRandomMultiplication, GenerateRandomConditional, GenerateRandomLog, GenerateRandomExponent, GenerateRandomModulo, GenerateRandomDivision
		};
		private static readonly int availableNodeTypes = generators.Length;
		public static Node GenerateRandomNode(ushort height){
			return generators[RandomNumberGenerator.GetInt32(0, availableNodeTypes)](height);
		}
		public static Node[] GenerateInitial(ushort pc, ushort complexity){
			if(pc >= complexity){
				throw new ArgumentException("Complexity must exceed the number of parameter expressions");
			}
			if(pc == 0){
				throw new ArgumentException("Minimum 1 input argument");
			}
			Node[] nodes = new Node[complexity];
			for(ushort i = 0; i < complexity; ++i){
				nodes[i] = i < pc ? new ArgumentNode(i) : GenerateRandomNode(i);
			}
			return nodes;
		}
		public static void RandomMutate(Node[] nodes, ushort offset){
			Queue<ushort> randomizeQueue = new Queue<ushort>();

			int target;
			do
			{
				target = RandomNumberGenerator.GetInt32(offset, nodes.Length);
			} while (nodes[target] is null);
			randomizeQueue.Enqueue((ushort)target);
			RandomizeImpl(nodes, randomizeQueue);
		}
		public static void StripStaticInvalidValues(Node[] nodes){
			int len = nodes.Length;
			Queue<ushort> randomizeQueue = new Queue<ushort>();
			while (true){
				JITCompiler.Optimize(nodes);
				bool added = false;
				for(ushort i = 1; i < len; ++i){
					Node node = nodes[i];
					if (node is null){
						continue;
					}
					if(node.TryEvaluate(nodes.AsSpan(0, i), out double result)){
						if(double.IsInfinity(result) | double.IsNaN(result)){
							randomizeQueue.Enqueue(i);
							added = true;
							continue;
						}
					}
					if (node is DivideOrModuloOperator divideOrModuloOperator)
					{
						if (divideOrModuloOperator.IsStaticInvalidOperator(nodes.AsSpan(0, i)))
						{
							randomizeQueue.Enqueue(i);
							added = true;
						}
					}
				}
				if(added){
					RandomizeImpl(nodes, randomizeQueue);
				} else{
					return;
				}

			}
		}

		public static void RandomizeImpl(Node[] nodes, Queue<ushort> randomizeQueue)
		{
			Dictionary<ushort, bool> keyValuePairs = new();
			while (randomizeQueue.TryDequeue(out ushort height))
			{
				if(keyValuePairs.TryAdd(height, false)){
					Node randomNode = GenerateRandomNode(height);
					foreach (ushort read in randomNode.GetReads())
					{
						if (nodes[read] is null)
						{
							randomizeQueue.Enqueue(read);
						}
					}
					nodes[height] = randomNode;
				}
			}
		}
	}
}
