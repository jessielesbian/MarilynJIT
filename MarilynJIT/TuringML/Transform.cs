using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Profiler;
using MarilynJIT.TuringML.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using MarilynJIT.TuringML.Transform.KellySSA;
using System.Text;
using System.Threading.Tasks;

namespace MarilynJIT.TuringML.Transform
{
	public sealed class KellySSAOptimizedVersionStripper : IVisitor{
		private KellySSAOptimizedVersionStripper(){
			
		}
		public static readonly KellySSAOptimizedVersionStripper instance = new KellySSAOptimizedVersionStripper();

		public TuringNode Visit(TuringNode turingNode)
		{
			if(turingNode is KellySSABasicBlock basicBlock){
				basicBlock.optimized = null;
			} else{
				turingNode.VisitChildren(this);
			}
			return turingNode;
		}
	}
	public sealed class RandomTransformer : IVisitor
	{
		private readonly ushort variablesCount;
		private readonly ushort basicBlockComplexity;

		public RandomTransformer(ushort variablesCount, ushort basicBlockComplexity)
		{
			this.variablesCount = variablesCount;
			this.basicBlockComplexity = basicBlockComplexity;
		}

		public static TuringNode GetRandomNode(ushort variablesCount, ushort basicBlockComplexity){
			int choice = RandomNumberGenerator.GetInt32(0, 4);
			switch(choice){
				case 0:
					return new MemoryRead { target = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount)};
				case 1:
					return new MemoryWrite { address = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount), value = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount) };
				default:
					TuringNode turingNode = new KellySSABasicBlock { nodes = Transformer.GenerateInitial(variablesCount, basicBlockComplexity) };
					if (choice == 3)
					{
						Block block = new Block();
						block.turingNodes.Add(turingNode);
						return new WhileBlock { underlying = block, condition = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount) };
					}
					return turingNode;
			}
		}
		public TuringNode Visit(TuringNode turingNode)
		{
			if(turingNode is Block block){
				int count = block.turingNodes.Count;
				if(count == 0){
					block.turingNodes.Add(GetRandomNode(variablesCount, basicBlockComplexity));
					return turingNode;
				}
				int target = RandomNumberGenerator.GetInt32(0, count);
				switch(RandomNumberGenerator.GetInt32(0, 3)){
					case 0:
						Visit(block.turingNodes[target]);
						return turingNode;
					case 1:
						block.turingNodes.Insert(target, GetRandomNode(variablesCount, basicBlockComplexity));
						return turingNode;
					default:
						block.turingNodes.RemoveAt(target);
						return turingNode;
				}
			}
			if(turingNode is KellySSABasicBlock basicBlock){
				Transformer.RandomMutate(basicBlock.nodes, variablesCount);
				basicBlock.optimized = null;
				return turingNode;
			}
			if(turingNode is WhileBlock whileBlock){
				if(RandomNumberGenerator.GetInt32(0, 2) == 0){
					whileBlock.condition = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount);
					return turingNode;
				}
			}
			if(turingNode is MemoryRead memoryRead){
				memoryRead.target = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount);
				return turingNode;
			}
			if (turingNode is MemoryWrite memoryWrite)
			{
				ushort n = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount);
				if(RandomNumberGenerator.GetInt32(0, 2) == 0){
					memoryWrite.address = n;
				} else{
					memoryWrite.value = n;
				}
				return turingNode;
			}
			turingNode.VisitChildren(this);
			return turingNode;
		}
	}
	
}
