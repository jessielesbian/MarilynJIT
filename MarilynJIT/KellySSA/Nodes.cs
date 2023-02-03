using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MarilynJIT.KellySSA.Nodes
{
	public abstract class Node{
		public virtual bool TryEvaluate(ReadOnlySpan<Node> nodes, out double result){
			result = 0;
			return false;
		}
		public abstract Expression Compile(ReadOnlySpan<Expression> prevNodes);
		public abstract IEnumerable<ushort> GetReads();
		public virtual Node Optimize(ReadOnlySpan<Node> nodes){
			if(TryEvaluate(nodes, out double result)){
				return new ConstantNode(result);
			}
			return this;
		}
	}
	public interface IDirectCompileNode{
		
	}
	public sealed class ConstantNode : Node, IDirectCompileNode{
		private readonly double value;
		public static readonly ConstantNode nan = new ConstantNode(double.NaN);
		public static readonly ConstantNode zero = new ConstantNode(0);
		public static readonly ConstantNode one = new ConstantNode(1);

		public ConstantNode(double value)
		{
			this.value = value;
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Constant(value, typeof(double));
		}
		public override IEnumerable<ushort> GetReads()
		{
			return Array.Empty<ushort>();
		}
		public override bool TryEvaluate(ReadOnlySpan<Node> otherNodes, out double result)
		{
			result = value;
			return true;
		}
		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			return this;
		}
	}
	public abstract class BinaryOperator : Node{
		protected readonly ushort first;
		protected readonly ushort second;

		public BinaryOperator(ushort first, ushort second)
		{
			this.first = first;
			this.second = second;
		}


		public override IEnumerable<ushort> GetReads()
		{
			yield return first;
			yield return second;
		}
		protected abstract double Calculate(double x, double y);
		public override bool TryEvaluate(ReadOnlySpan<Node> nodes, out double result)
		{
			if(nodes[first].TryEvaluate(nodes.Slice(0, first), out double x)){
				if (nodes[second].TryEvaluate(nodes.Slice(0, second), out double y))
				{
					result = Calculate(x, y);
					return true;
				}
			}
			result = 0;
			return false;
		}
	}
	public sealed class AddidionOperator : BinaryOperator
	{
		public AddidionOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Add(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return x + y;
		}

		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			Node firstnode = nodes[first];
			bool hasx = firstnode.TryEvaluate(nodes.Slice(0, first), out double x);
			Node secnode = nodes[second];
			bool hasy = secnode.TryEvaluate(nodes.Slice(0, second), out double y);

			if(hasx)
			{
				if (hasy)
				{
					return new ConstantNode(x + y);
				}
				if (double.IsNaN(x)){
					return ConstantNode.nan;
				}
				if(x == 0){
					return secnode is IDirectCompileNode ? secnode : new Move(second);
				}
			}
			if (hasy)
			{
				if(double.IsNaN(y)){
					return ConstantNode.nan;
				}
				if (y == 0)
				{
					return firstnode is IDirectCompileNode ? firstnode : new Move(first);
				}
			}

			return this;
		}
	}
	public sealed class SubtractionOperator : BinaryOperator
	{
		public SubtractionOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Subtract(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return x - y;
		}
		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			Node firstnode = nodes[first];
			bool hasx = firstnode.TryEvaluate(nodes.Slice(0, first), out double x);
			Node secnode = nodes[second];
			bool hasy = secnode.TryEvaluate(nodes.Slice(0, second), out double y);

			//Known inputs
			
			if (hasx)
			{
				if (hasy)
				{
					return new ConstantNode(x - y);
				}
				if (double.IsNaN(x)){
					return ConstantNode.nan;
				}
				
			}
			if (hasy)
			{
				if(double.IsNaN(y)){
					return ConstantNode.nan;
				}
				if (y == 0)
				{
					return firstnode is IDirectCompileNode ? firstnode : new Move(first);
				}
			}
			return this;
		}
	}
	public sealed class MultiplyOperator : BinaryOperator
	{
		public MultiplyOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Multiply(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return x * y;
		}
		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			Node firstnode = nodes[first];
			bool hasx = firstnode.TryEvaluate(nodes.Slice(0, first), out double x);
			Node secnode = nodes[second];
			bool hasy = secnode.TryEvaluate(nodes.Slice(0, second), out double y);

			if (hasx)
			{
				if (hasy)
				{
					return new ConstantNode(x * y);
				}
				if (double.IsNaN(x)){
					return ConstantNode.nan;
				}
				if (x == 1)
				{
					return secnode is IDirectCompileNode ? secnode : new Move(second);
				}
			}
			if (hasy)
			{
				if(double.IsNaN(y)){
					return ConstantNode.nan;
				}
				if (y == 1)
				{
					return firstnode is IDirectCompileNode ? firstnode : new Move(first);
				}
			}

			//Multiply by zero
			if ((hasx & x == 0) | (hasy & y == 0)){
				return ConstantNode.zero;
			}
			return this;
		}
	}
	public sealed class DivideOperator : BinaryOperator
	{
		public DivideOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Divide(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return x / y;
		}

		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			Node firstnode = nodes[first];
			bool hasx = firstnode.TryEvaluate(nodes.Slice(0, first), out double x);
			Node secnode = nodes[second];
			bool hasy = secnode.TryEvaluate(nodes.Slice(0, second), out double y);

			if (hasx)
			{
				if (hasy)
				{
					return new ConstantNode(x / y);
				}
				if (double.IsNaN(x)){
					return ConstantNode.nan;
				}
				if (x == 1)
				{
					return secnode is IDirectCompileNode ? secnode : new Move(second);
				}
			}
			if (hasy)
			{
				if(double.IsNaN(y)){
					return ConstantNode.nan;
				}
				if (y == 1)
				{
					return firstnode is IDirectCompileNode ? firstnode : new Move(first);
				}
			}

			//Bail out since we can't optimize
			return this;
		}
	}
	public sealed class ModuloOperator : BinaryOperator{
		public ModuloOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Modulo(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return x % y;
		}
	}
	public sealed class ExponentOperator : BinaryOperator
	{
		public ExponentOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Power(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return Math.Pow(x, y);
		}
		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			Node firstnode = nodes[first];
			bool hasx = firstnode.TryEvaluate(nodes.Slice(0, first), out double x);
			Node secnode = nodes[second];
			bool hasy = secnode.TryEvaluate(nodes.Slice(0, second), out double y);

			if (hasx)
			{
				if(hasy){
					return new ConstantNode(Math.Pow(x, y));
				}
				if(double.IsNaN(x)){
					return ConstantNode.nan;
				}
				if (x == 1)
				{
					return ConstantNode.one;
				}
			}
			if (hasy)
			{
				if (double.IsNaN(y))
				{
					return ConstantNode.nan;
				}
				if (y == 0)
				{
					return ConstantNode.one;
				}
			}

			return this;
		}
	}
	public sealed class LogOperator : BinaryOperator
	{
		private static readonly MethodInfo methodInfo = typeof(Math).GetMethod("Log", types: new Type[] {typeof(double), typeof(double)});
		public LogOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return Expression.Call(methodInfo, prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return Math.Log(x, y);
		}
	}
	public sealed class Move : Node, IDirectCompileNode{
		private readonly ushort target;

		public Move(ushort target)
		{
			this.target = target;
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return prevNodes[target];
		}

		public override IEnumerable<ushort> GetReads()
		{
			yield return target;
		}
	}

	public sealed class Conditional : Node{
		private readonly ushort x;
		private readonly ushort y;
		private readonly ushort z;
		private static readonly Expression zero = Expression.Constant((double)0, typeof(double));

		public Conditional(ushort x, ushort y, ushort z)
		{
			this.x = x;
			this.y = y;
			this.z = z;
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			ParameterExpression variable = Expression.Variable(typeof(double));
			return Expression.Block(new ParameterExpression[] { variable }, Expression.IfThenElse(Expression.GreaterThan(prevNodes[z], zero), Expression.Assign(variable, prevNodes[x]), Expression.Assign(variable, prevNodes[y])), variable);
		}
		public Expression CompileProfiling(ReadOnlySpan<Expression> prevNodes, Expression taken, Expression notTaken)
		{
			ParameterExpression variable = Expression.Variable(typeof(double));
			return Expression.Block(new ParameterExpression[] { variable }, Expression.IfThenElse(Expression.GreaterThan(prevNodes[z], zero), Expression.Block(taken, Expression.Assign(variable, prevNodes[x])), Expression.Block(notTaken, Expression.Assign(variable, prevNodes[y]))), variable);
		}
		public override IEnumerable<ushort> GetReads()
		{
			yield return x;
			yield return y;
			yield return z;
		}
		public override bool TryEvaluate(ReadOnlySpan<Node> nodes, out double result)
		{
			if(nodes[z].TryEvaluate(nodes.Slice(0, z), out double zv)){
				ushort offset = zv > 0 ? x : y;
				return nodes[offset].TryEvaluate(nodes.Slice(0, offset), out result);
			}
			result = 0;
			return false;
		}
		public override Node Optimize(ReadOnlySpan<Node> nodes)
		{
			if (nodes[z].TryEvaluate(nodes.Slice(0, z), out double zv))
			{
				return If(nodes, zv > 0);
			}
			return this;
		}
		public Node If(ReadOnlySpan<Node> nodes, bool taken){
			ushort offset = taken ? x : y;
			Node node = nodes[offset].Optimize(nodes.Slice(0, offset));
			if (node is IDirectCompileNode)
			{
				return node;
			}
			return new Move(offset);
		}
	}
	public interface IRemovalProtectedNode{
		
	}
	public sealed class ArgumentNode : Node, IDirectCompileNode, IRemovalProtectedNode
	{
		private readonly ParameterExpression expression;

		public ArgumentNode(ParameterExpression expression)
		{
			this.expression = expression ?? throw new ArgumentNullException(nameof(expression));
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes)
		{
			return expression;
		}

		public override IEnumerable<ushort> GetReads()
		{
			return Array.Empty<ushort>();
		}
	}

}