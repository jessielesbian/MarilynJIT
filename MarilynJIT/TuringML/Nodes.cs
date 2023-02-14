using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;
using Newtonsoft.Json;

namespace MarilynJIT.TuringML.Nodes
{
	public interface IVisitor{
		public TuringNode Visit(TuringNode turingNode);
	}
	[Serializable]
	public abstract class TuringNode{
		public abstract Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator? profilingCodeGenerator);
		public virtual void VisitChildren(IVisitor visitor){
			
		}
		public virtual TuringNode PrepareForSerialization(){
			foreach(object obj in GetType().GetCustomAttributes(false)){
				if(obj is SerializableAttribute){
					return this;
				}
			}
			throw new Exception("This node is non-serializable");
		}
		public virtual TuringNode AfterDeserialization(){
			return this;
		}
		protected abstract TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair);
		private sealed class BlackHoleDictionary : IDictionary<TuringNode, TuringNode>
		{
			public TuringNode this[TuringNode key] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

			public ICollection<TuringNode> Keys => throw new NotImplementedException();

			public ICollection<TuringNode> Values => throw new NotImplementedException();

			public int Count => throw new NotImplementedException();

			public bool IsReadOnly => throw new NotImplementedException();

			public void Add(TuringNode key, TuringNode value)
			{
				
			}

			public void Add(KeyValuePair<TuringNode, TuringNode> item)
			{
				throw new NotImplementedException();
			}

			public void Clear()
			{
				throw new NotImplementedException();
			}

			public bool Contains(KeyValuePair<TuringNode, TuringNode> item)
			{
				throw new NotImplementedException();
			}

			public bool ContainsKey(TuringNode key)
			{
				throw new NotImplementedException();
			}

			public void CopyTo(KeyValuePair<TuringNode, TuringNode>[] array, int arrayIndex)
			{
				throw new NotImplementedException();
			}

			public IEnumerator<KeyValuePair<TuringNode, TuringNode>> GetEnumerator()
			{
				throw new NotImplementedException();
			}

			public bool Remove(TuringNode key)
			{
				throw new NotImplementedException();
			}

			public bool Remove(KeyValuePair<TuringNode, TuringNode> item)
			{
				throw new NotImplementedException();
			}

			public bool TryGetValue(TuringNode key, [MaybeNullWhen(false)] out TuringNode value)
			{
				throw new NotImplementedException();
			}

			IEnumerator IEnumerable.GetEnumerator()
			{
				throw new NotImplementedException();
			}
		}
		public TuringNode DeepClone()
		{
			return DeepCloneIMPL(new Dictionary<TuringNode, TuringNode>(ReferenceEqualityComparer.Instance));
		}
		public TuringNode DeepClone(IDictionary<TuringNode, TuringNode> keyValuePairs)
		{
			TuringNode cloned = DeepCloneIMPL(keyValuePairs);
			keyValuePairs.Add(cloned, this);
			return cloned;
		}
	}
	public sealed class ProfilingCode : TuringNode
	{
		private readonly TuringNode underlying;

		public ProfilingCode(TuringNode underlying)
		{
			this.underlying = underlying ?? throw new ArgumentNullException(nameof(underlying));
		}

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			return underlying.Compile(variables, safepoint, memoryArray, profilingCodeGenerator);
		}

		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			return new ProfilingCode(underlying.DeepClone(keyValuePair));
		}
	}
	[Serializable]
	public sealed class NoOperation : TuringNode{
		private static readonly Expression expression = Expression.Block();

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			return expression;
		}

		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			return new NoOperation();
		}
	}
	[Serializable]
	public sealed class WhileBlock : TuringNode{
		public TuringNode underlying;
		public ushort condition;
		private static readonly Expression zero = Expression.Constant(0.0, typeof(double));

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			LabelTarget breakTarget = Expression.Label();
			return Expression.Loop(Expression.IfThenElse(Expression.GreaterThan(variables[condition], zero), Expression.Block(safepoint, underlying.Compile(variables, safepoint, memoryArray, profilingCodeGenerator)), Expression.Break(breakTarget)), breakTarget);
		}

		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			return new WhileBlock { underlying = underlying.DeepClone(keyValuePair), condition = condition};
		}
		public override void VisitChildren(IVisitor visitor)
		{
			underlying = visitor.Visit(underlying);
		}
	}

	public sealed class Block : TuringNode{
		[Serializable]
		private sealed class SerializableBlock : TuringNode
		{
			public TuringNode[] turingNodes;

			public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
			{
				throw new NotImplementedException();
			}

			protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
			{
				throw new NotImplementedException();
			}
			public override TuringNode AfterDeserialization()
			{
				Block block = new Block();
				block.turingNodes.AddRange(turingNodes);
				return block;
			}
		}
		public override TuringNode PrepareForSerialization()
		{
			return new SerializableBlock { turingNodes = turingNodes.ToArray() };
		}
		public readonly List<TuringNode> turingNodes = new List<TuringNode>();

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			List<Expression> compiled = new List<Expression>(turingNodes.Count);
			foreach(TuringNode turingNode in turingNodes){
				if(turingNode is NoOperation){
					continue;
				}
				compiled.Add(turingNode.Compile(variables, safepoint, memoryArray, profilingCodeGenerator));
			}
			return Expression.Block(compiled);
		}
		private static IEnumerable<TuringNode> DeepCopyChildNodes(List<TuringNode> turingNodes, IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			foreach(TuringNode turingNode in turingNodes){
				TuringNode turingNode1 = turingNode.DeepClone(keyValuePair);
				if(turingNode1 is NoOperation){
					continue;
				}
				yield return turingNode1;
			}
		}
		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			Block block = new Block();
			block.turingNodes.AddRange(DeepCopyChildNodes(turingNodes, keyValuePair));
			return block;
		}
		public override void VisitChildren(IVisitor visitor)
		{
			Queue<TuringNode> queue = new();
			foreach(TuringNode turingNode in turingNodes){
				TuringNode turingNode1 = visitor.Visit(turingNode);
				if(turingNode1 is NoOperation){
					continue;
				}
				queue.Enqueue(turingNode1);
			}
			turingNodes.Clear();
			while(queue.TryDequeue(out TuringNode turingNode2)){
				turingNodes.Add(turingNode2);
			}
		}
	}
	[Serializable]
	public sealed class MemoryRead : TuringNode{
		
		private static readonly MethodInfo read = typeof(VirtualMemoryManager).GetMethod("Read");
		public ushort target;

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			ParameterExpression variable = variables[target];
			return Expression.Assign(variable, Expression.Call(memoryArray, read, variable));
		}

		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			return new MemoryRead { target = target };
		}
	}
	[Serializable]
	public sealed class MemoryWrite : TuringNode{
		public ushort address;
		public ushort value;
		
		private static readonly MethodInfo write = typeof(VirtualMemoryManager).GetMethod("Write");
		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			return Expression.Call(memoryArray, write, variables[address], variables[value]);
		}

		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			return new MemoryWrite { address = address, value = value };
		}
	}
	
	public sealed class KellySSABasicBlock : TuringNode
	{
		[Serializable]
		private sealed class SerializableKellySSABasicBlock : TuringNode
		{
			public string json;

			public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
			{
				throw new NotImplementedException();
			}

			protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
			{
				throw new NotImplementedException();
			}
			public override TuringNode AfterDeserialization()
			{
				return new KellySSABasicBlock { nodes = KellySSA.Nodes.Serialization.DeserializeJsonNodesArray(json) };
			}
		}
		public Node[] nodes;
		public Node[] optimized;

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			Expression compiled = KellySSA.JITCompiler.Compile(nodes, variables, profilingCodeGenerator: profilingCodeGenerator);
			if(optimized is null){
				return compiled;
			}
			return Expression.TryCatch(KellySSA.JITCompiler.Compile(optimized, variables), Expression.MakeCatchBlock(typeof(OptimizedBailout), null, compiled, null));

		}
		private static Node[] Copy(Node[] nodes){
			if(nodes is null){
				return null;
			}
			return (Node[])nodes.Clone();
		}
		protected override TuringNode DeepCloneIMPL(IDictionary<TuringNode, TuringNode> keyValuePair)
		{
			return new KellySSABasicBlock { nodes = Copy(nodes), optimized = Copy(optimized) };
		}
		public override TuringNode PrepareForSerialization()
		{
			return new SerializableKellySSABasicBlock { json = JsonConvert.SerializeObject(nodes) };
		}
	}
}
