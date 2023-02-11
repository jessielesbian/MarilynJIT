using MarilynJIT.KellySSA;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.Serialization.Formatters.Binary;

namespace MarilynJIT.TuringML.Nodes
{
	public static class Serialization
	{
		private sealed class SerializableBlock : TuringNode
		{
			public TuringNode[] turingNodes;

			public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, IProfilingCodeGenerator profilingCodeGenerator)
			{
				throw new NotImplementedException();
			}

			public override TuringNode DeepClone()
			{
				throw new NotImplementedException();
			}
		}
		private sealed class SerializableKellySSABasicBlock : TuringNode
		{
			public string json;

			public override Expression Compile(ReadOnlySpan<ParameterExpression> variables, Expression safepoint, IProfilingCodeGenerator profilingCodeGenerator)
			{
				throw new NotImplementedException();
			}

			public override TuringNode DeepClone()
			{
				throw new NotImplementedException();
			}
		}
		private sealed class BeforeSerialize : IVisitor
		{
			public TuringNode Visit(TuringNode turingNode)
			{
				turingNode.VisitChildren(this);
				if (turingNode is Block block)
				{
					SerializableBlock serializableBlock = new SerializableBlock();
					serializableBlock.turingNodes = block.turingNodes.ToArray();
					return serializableBlock;
				}
				if (turingNode is KellySSABasicBlock kellySSABasicBlock)
				{
					SerializableKellySSABasicBlock serializableKellySSABasicBlock = new();
					serializableKellySSABasicBlock.json = JsonConvert.SerializeObject(kellySSABasicBlock.nodes);
					return serializableKellySSABasicBlock;
				}
				return turingNode;
			}
		}
		private sealed class AfterSerialize : IVisitor
		{
			public TuringNode Visit(TuringNode turingNode)
			{
				turingNode.VisitChildren(this);
				if (turingNode is SerializableBlock serializableBlock)
				{
					Block block = new Block();
					block.turingNodes.AddRange(serializableBlock.turingNodes);
					return block;
				}
				if (turingNode is SerializableKellySSABasicBlock serializableKellySSABasicBlock)
				{
					KellySSABasicBlock kellySSABasicBlock = new();
					kellySSABasicBlock.nodes = KellySSA.Nodes.Serialization.DeserializeJsonNodesArray(serializableKellySSABasicBlock.json);
					return kellySSABasicBlock;
				}
				return turingNode;
			}
		}

		private static readonly IVisitor beforeSerialize = new BeforeSerialize();
		private static readonly IVisitor afterSerialize = new AfterSerialize();
		public static void Serialize(TuringNode turingNode, Stream stream)
		{
#pragma warning disable SYSLIB0011 // Type or member is obsolete
			new BinaryFormatter().Serialize(stream, beforeSerialize.Visit(turingNode.DeepClone()));
#pragma warning restore SYSLIB0011 // Type or member is obsolete
		}

		public static TuringNode Deserialize(Stream stream)
		{
#pragma warning disable SYSLIB0011 // Type or member is obsolete
			return afterSerialize.Visit((TuringNode)new BinaryFormatter().Deserialize(stream));
#pragma warning restore SYSLIB0011 // Type or member is obsolete
		}
	}
}
