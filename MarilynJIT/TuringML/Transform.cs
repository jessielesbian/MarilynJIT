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
			TuringNode turingNode = new KellySSABasicBlock { nodes = Transformer.GenerateInitial(variablesCount, basicBlockComplexity) };
			if(RandomNumberGenerator.GetInt32(0, 2) == 0){
				Block block = new Block();
				block.turingNodes.Add(turingNode);
				return new WhileBlock { underlying = block, condition = (ushort) RandomNumberGenerator.GetInt32(0, variablesCount)};
			}
			return turingNode;
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
				if(RandomNumberGenerator.GetInt32(0, 2) == 0){
					Visit(block.turingNodes[target]);
				} else{
					block.turingNodes.Insert(target, GetRandomNode(variablesCount, basicBlockComplexity));
				}
				return turingNode;
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
			turingNode.VisitChildren(this);
			return turingNode;
		}
	}
	
}
