using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using MarilynJIT.KellySSA.Profiler;
using MarilynJIT.TuringML.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace MarilynJIT.TuringML.Transform.KellySSA
{
	public sealed class UnreachedBranchesStripper : IVisitor
	{
		private readonly ulong treshold;
		private readonly bool bailout;
		private readonly ushort offset;
		private readonly IColdCodeStripper coldCodeStripper;

		public UnreachedBranchesStripper(ulong treshold, bool bailout, ushort offset, IColdCodeStripper coldCodeStripper)
		{
			this.treshold = treshold;
			this.bailout = bailout;
			this.offset = offset;
			this.coldCodeStripper = coldCodeStripper ?? throw new ArgumentNullException(nameof(coldCodeStripper));
		}

		public TuringNode Visit(TuringNode turingNode)
		{
			if (turingNode is KellySSABasicBlock basicBlock)
			{
				if (bailout)
				{
					basicBlock.optimized = (Node[])basicBlock.nodes.Clone();
					coldCodeStripper.Strip(basicBlock.optimized, offset, treshold, true);
				}
				else
				{
					basicBlock.optimized = null;
					coldCodeStripper.Strip(basicBlock.nodes, offset, treshold, false);
				}
			}
			else
			{
				turingNode.VisitChildren(this);
			}
			return turingNode;
		}
	}
	public static class Transformer
	{
		public static Node[] GenerateInitial(ushort variablesCount, ushort complexity)
		{
			if (variablesCount >= complexity)
			{
				throw new ArgumentException("Complexity must exceed the number of parameter expressions");
			}
			if (variablesCount == 0)
			{
				throw new ArgumentException("Minimum 1 input argument");
			}
			Node[] nodes = new Node[complexity];
			ushort cmplx1 = complexity--;
			for (ushort i = 0; i < complexity; ++i)
			{
				nodes[i] = i < variablesCount ? new ArgumentNode(i) : RandomProgramGenerator.GenerateRandomNode(i);
			}
			nodes[complexity] = new CopyResults(complexity, (ushort)(complexity - variablesCount));
			RandomProgramGenerator.StripStaticInvalidValues(nodes);
			return nodes;
		}
		public static void RandomMutate(Node[] nodes, ushort offset)
		{
			Queue<ushort> randomizeQueue = new Queue<ushort>();

			int target;
			do
			{
				target = RandomNumberGenerator.GetInt32(offset, nodes.Length - 1);
			} while (nodes[target] is null);
			randomizeQueue.Enqueue((ushort)target);
			RandomProgramGenerator.RandomizeImpl(nodes, randomizeQueue);
			RandomProgramGenerator.StripStaticInvalidValues(nodes);
		}


		/// <summary>
		/// Exists solely for deserialization purposes
		/// </summary>
		internal static Node CreateCopyResults(ushort size, ushort start)
		{
			return new CopyResults(size, start);
		}
		private sealed class CopyResults : Node, IDirectCompileNode
		{
			private readonly ushort size;
			private readonly ushort start;


			public CopyResults(ushort size, ushort start)
			{
				this.size = size;
				this.start = start;
			}

			public override Expression Compile(ReadOnlySpan<Expression> prevNodes, ReadOnlySpan<ParameterExpression> parameterExpressions)
			{
				Expression[] expressions = new Expression[size - start];
				ushort x = 0;
				for (ushort i = start; i < size; ++i,++x)
				{
					expressions[x] = Expression.Assign(parameterExpressions[x], prevNodes[i]);
				}
				return Expression.Block(expressions);
			}

			public override IEnumerable<ushort> GetReads()
			{
				for (ushort i = start; i < size; ++i)
				{
					yield return i;
				}
			}
		}



	}
}
