using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace MarilynJIT.KellySSA.Nodes
{
	[JsonObject(MemberSerialization.Fields)]
	public abstract class Node{
		private readonly string typeName;
		public Node(){
			typeName = GetType().Name;
		}
		public virtual bool TryEvaluate(ReadOnlySpan<Node> nodes, out double result){
			result = 0;
			return false;
		}
		public abstract Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray);
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

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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
	public sealed class AdditionOperator : BinaryOperator
	{
		public AdditionOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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
	public sealed class DynamicInvalidOperation : Exception{
		public readonly ushort offset;

		public DynamicInvalidOperation(ushort offset)
		{
			this.offset = offset;
		}
	}
	public interface ICheckedOperator{
		public Expression CompileWithChecking(ReadOnlySpan<Expression> prevNodes, ushort index);
		public bool IsStaticInvalidOperator(ReadOnlySpan<Node> prevNodes);
	}
	public abstract class DivideOrModuloOperator : BinaryOperator, ICheckedOperator
	{
		private static readonly ConstructorInfo dynamicZeroDivisionConstructor = typeof(DynamicInvalidOperation).GetConstructor(new Type[] {typeof(ushort)});
		protected DivideOrModuloOperator(ushort first, ushort second) : base(first, second)
		{
		}

		private static readonly Expression zero = Expression.Constant(0.0, typeof(double));

		public Expression CompileWithChecking(ReadOnlySpan<Expression> prevNodes, ushort index){
			return Expression.Block(Expression.IfThen(Expression.Equal(prevNodes[second], zero), Expression.Throw(Expression.New(dynamicZeroDivisionConstructor, Expression.Constant(index, typeof(ushort))))), Compile(prevNodes, null));
		}
		public bool IsStaticInvalidOperator(ReadOnlySpan<Node> prevNodes){
			if (prevNodes[second].TryEvaluate(prevNodes, out double result))
			{
				return result == 0;
			}
			return false;
		}
	}
	public sealed class DivideOperator : DivideOrModuloOperator
	{
		public DivideOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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
	public sealed class ModuloOperator : DivideOrModuloOperator{
		public ModuloOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
		{
			return Expression.Modulo(prevNodes[first], prevNodes[second]);
		}

		protected override double Calculate(double x, double y)
		{
			return x % y;
		}
	}
	public abstract class LogOrExponentOperator : BinaryOperator, ICheckedOperator
	{
		private static readonly ConstructorInfo dynamicZeroDivisionConstructor = typeof(DynamicInvalidOperation).GetConstructor(new Type[] { typeof(ushort) });
		protected LogOrExponentOperator(ushort first, ushort second) : base(first, second)
		{
		}

		private static readonly Expression zero = Expression.Constant(0.0, typeof(double));

		public Expression CompileWithChecking(ReadOnlySpan<Expression> prevNodes, ushort index)
		{
			return Expression.Block(Expression.IfThen(Expression.LessThanOrEqual(prevNodes[second], zero), Expression.Throw(Expression.New(dynamicZeroDivisionConstructor, Expression.Constant(index, typeof(ushort))))), Compile(prevNodes, null));
		}
		public bool IsStaticInvalidOperator(ReadOnlySpan<Node> prevNodes)
		{
			if (prevNodes[first].TryEvaluate(prevNodes, out double result))
			{
				return result <= 0;
			}
			return false;
		}
	}
	public sealed class ExponentOperator : LogOrExponentOperator
	{
		public ExponentOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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
	public sealed class LogOperator : LogOrExponentOperator
	{
		private static readonly MethodInfo methodInfo = typeof(Math).GetMethod("Log", types: new Type[] {typeof(double), typeof(double)});
		public LogOperator(ushort first, ushort second) : base(first, second)
		{
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
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
				return If(nodes, zv > 0, false);
			}
			return this;
		}
		public Node If(ReadOnlySpan<Node> nodes, bool taken, bool emitBailout){
			ushort offset = taken ? x : y;
			if(emitBailout){
				return new Bailout(offset, z, taken);
			}
			Node node = nodes[offset].Optimize(nodes.Slice(0, offset));
			if (node is IDirectCompileNode)
			{
				return node;
			}
			return new Move(offset);
		}

	}
	public sealed class Bailout : Node
	{
		private static readonly Expression zero = Expression.Constant((double)0, typeof(double));
		private readonly ushort mybranch;
		private readonly ushort z;
		private readonly bool taken;
		private static readonly Expression throwBailout = Expression.Throw(Expression.New(typeof(OptimizedBailout)));

		public Bailout(ushort mybranch, ushort z, bool taken)
		{
			this.mybranch = mybranch;
			this.z = z;
			this.taken = taken;
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
		{
			return Expression.Block(Expression.IfThen(taken ? Expression.LessThanOrEqual(prevNodes[z], zero) : Expression.GreaterThan(prevNodes[z], zero), throwBailout), prevNodes[mybranch]);
		}

		public override IEnumerable<ushort> GetReads()
		{
			yield return z;
			yield return mybranch;
		}
		public override bool TryEvaluate(ReadOnlySpan<Node> nodes, out double result)
		{
			if (nodes[z].TryEvaluate(nodes.Slice(0, z), out double value))
			{
				if ((value > 0) == taken)
				{
					return nodes[mybranch].TryEvaluate(nodes.Slice(0, mybranch), out result);
				}
			}
			result = 0;
			return false;
		}
	}

	public interface IRemovalProtectedNode{
		
	}
	public sealed class ArgumentNode : Node, IRemovalProtectedNode
	{
		private readonly ushort parameterId;

		public ArgumentNode(ushort parameterId)
		{
			this.parameterId = parameterId;
		}

		public override Expression Compile(ReadOnlySpan<Expression> prevNodes, Expression inputArray)
		{
			return Expression.ArrayAccess(inputArray, Expression.Constant((int) parameterId, typeof(int)));
		}

		public override IEnumerable<ushort> GetReads()
		{
			return Array.Empty<ushort>();
		}
	}

}