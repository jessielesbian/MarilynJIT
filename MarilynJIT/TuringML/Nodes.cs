using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Cryptography;
using MarilynJIT.KellySSA;
using MarilynJIT.KellySSA.Nodes;

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
		public abstract TuringNode DeepClone();
	}
	[Serializable]
	public sealed class NoOperation : TuringNode{
		private static readonly Expression expression = Expression.Block();

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			return expression;
		}

		public override TuringNode DeepClone()
		{
			return this;
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

		public override TuringNode DeepClone()
		{
			return new WhileBlock { underlying = underlying.DeepClone(), condition = condition};
		}
		public override void VisitChildren(IVisitor visitor)
		{
			underlying = visitor.Visit(underlying);
		}
	}

	public sealed class Block : TuringNode{
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
		private static IEnumerable<TuringNode> DeepCopyChildNodes(List<TuringNode> turingNodes){
			foreach(TuringNode turingNode in turingNodes){
				TuringNode turingNode1 = turingNode.DeepClone();
				if(turingNode1 is NoOperation){
					continue;
				}
				yield return turingNode1;
			}
		}
		public override TuringNode DeepClone()
		{
			Block block = new Block();
			block.turingNodes.AddRange(DeepCopyChildNodes(turingNodes));
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

		public override TuringNode DeepClone()
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

		public override TuringNode DeepClone()
		{
			return new MemoryWrite { address = address, value = value };
		}
	}
	
	public sealed class KellySSABasicBlock : TuringNode
	{
		public Node[] nodes;
		public Node[] optimized;

		public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, ParameterExpression memoryArray, IProfilingCodeGenerator profilingCodeGenerator)
		{
			Expression compiled = KellySSA.JITCompiler.Compile(nodes, variables);
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
		public override TuringNode DeepClone()
		{
			return new KellySSABasicBlock { nodes = Copy(nodes), optimized = Copy(optimized) };
		}
	}
}
