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
using System.Reflection;

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
		private readonly ushort argumentsCount;

		public RandomTransformer(ushort variablesCount, ushort basicBlockComplexity, ushort argumentsCount)
		{
			this.variablesCount = variablesCount;
			this.basicBlockComplexity = basicBlockComplexity;
			this.argumentsCount = argumentsCount;
		}

		public static TuringNode GetRandomNode(ushort variablesCount, ushort basicBlockComplexity, ushort argumentsCount){
			int choice = RandomNumberGenerator.GetInt32(0, 4);
			switch(choice){
				case 0:
					return new MemoryRead { target = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount)};
				case 1:
					return new MemoryWrite { address = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount), value = (ushort)RandomNumberGenerator.GetInt32(0, variablesCount) };
				default:
					TuringNode turingNode = new KellySSABasicBlock { nodes = Transformer.GenerateInitial(variablesCount, basicBlockComplexity, argumentsCount) };
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
					block.turingNodes.Add(GetRandomNode(variablesCount, basicBlockComplexity, argumentsCount));
					return turingNode;
				}
				int target = RandomNumberGenerator.GetInt32(0, count);
				switch(RandomNumberGenerator.GetInt32(0, 16)){
					case 0:
						block.turingNodes.RemoveAt(target);
						return turingNode;
					case 1:
						block.turingNodes.Insert(target, GetRandomNode(variablesCount, basicBlockComplexity, argumentsCount));
						return turingNode;
					default:
						
						Visit(block.turingNodes[target]);
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
	public sealed class NodeGrabber<T> : IVisitor where T : TuringNode
	{
		private readonly Queue<T> queue;
		public NodeGrabber(Queue<T> queue)
		{
			this.queue = queue ?? throw new ArgumentNullException(nameof(queue));
		}

		public TuringNode Visit(TuringNode turingNode)
		{
			if(turingNode is T grabbed){
				queue.Enqueue(grabbed);
			}
			turingNode.VisitChildren(this);
			return turingNode;
		}
	}
	public sealed class MassRemovalVisitor<T> : IVisitor where T : TuringNode{
		public static readonly IVisitor instance = new MassRemovalVisitor<T>();
		private MassRemovalVisitor(){
			
		}

		public TuringNode Visit(TuringNode turingNode)
		{
			if(turingNode is T){
				return new NoOperation();
			}
			turingNode.VisitChildren(this);
			return turingNode;
		}
	}
	public sealed class ThreadPrivateProfiler : IDisposable{

		public readonly IVisitor livenessProfilingCodeInjector;
		public readonly IVisitor unreachedLoopsStripper;
		private ThreadPrivateProfiler(){
			unreachedLoopsStripper = new UnreachedLoopsStripper(this, unreachedCode);
			livenessProfilingCodeInjector = new LivenessProfilingCodeInjector(unreachedCode);
		}

		public static ThreadPrivateProfiler Create(){
			if(threadPrivateProfiler is null){
				ThreadPrivateProfiler temp = new ThreadPrivateProfiler();
				threadPrivateProfiler = temp;
				return temp;
			} else{
				throw new Exception("This thread has already created a thread-private profiler");
			}
		}
		
		[ThreadStatic]
		private static ThreadPrivateProfiler threadPrivateProfiler;

		private readonly Dictionary<int, Block> unreachedCode = new Dictionary<int, Block>();

		private static void Remove(int id)
		{
			threadPrivateProfiler.unreachedCode.Remove(id);
		}

		private static void CheckBelongToThread(ThreadPrivateProfiler me){
			if(ReferenceEquals(threadPrivateProfiler, me)){
				return;
			}
			throw new Exception("This thread-private profiler does not belong to this thread");
		}

		public void Dispose()
		{
			CheckBelongToThread(this);
			threadPrivateProfiler = null;
		}

		private static readonly MethodInfo remove = new Action<int>(Remove).Method;
		private sealed class MarkReachableNode : TuringNode
		{
			private readonly int id;

			public MarkReachableNode(int id)
			{
				this.id = id;
			}

			public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
			{
				return Expression.Call(remove, Expression.Constant(id, typeof(int)));
			}

			protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
			{
				return new MarkReachableNode(id);
			}
		}

		private sealed class LivenessProfilingCodeInjector : IVisitor
		{
			private readonly Dictionary<TuringNode, bool> knownNodes = new Dictionary<TuringNode, bool>(ReferenceEqualityComparer.Instance);
			private readonly Dictionary<int, Block> unreachedCode;

			public LivenessProfilingCodeInjector(Dictionary<int, Block> unreachedCode)
			{
				this.unreachedCode = unreachedCode;
			}

			public TuringNode Visit(TuringNode turingNode)
			{
				if (knownNodes.TryAdd(turingNode, false))
				{
					if (turingNode is Block block)
					{
						int id = knownNodes.Count;
						block.turingNodes.Add(new ProfilingCode(new MarkReachableNode(id)));
						unreachedCode.Add(id, block);
					}
					turingNode.VisitChildren(this);
				}
				return turingNode;
			}
		}
		private sealed class UnreachedLoopsStripper : IVisitor
		{
			private readonly ThreadPrivateProfiler parent;

			public UnreachedLoopsStripper(ThreadPrivateProfiler parent, Dictionary<int, Block> unreachedCode)
			{
				this.parent = parent;
				this.unreachedCode = unreachedCode;
			}
			private Dictionary<TuringNode, bool> blacklist;
			private static readonly TuringNode nop = new NoOperation();
			private readonly Dictionary<int, Block> unreachedCode;
			public TuringNode Visit(TuringNode turingNode)
			{
				bool root = blacklist is null;
				if (root){
					CheckBelongToThread(parent);
					blacklist = new Dictionary<TuringNode, bool>(ReferenceEqualityComparer.Instance);
					foreach (TuringNode turingNode1 in unreachedCode.Values)
					{
						blacklist.Add(turingNode1, false);
					}
				}
				if (turingNode is WhileBlock whileBlock)
				{
					if (blacklist.ContainsKey(whileBlock.underlying))
					{
						if(root){
							blacklist = null;
						}
						return nop;
					}
				}
				turingNode.VisitChildren(this);
				if (root)
				{
					blacklist = null;
				}
				return turingNode;

			}
		}
	}
}
