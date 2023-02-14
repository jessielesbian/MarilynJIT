using MarilynJIT.KellySSA;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq.Expressions;
using System.Runtime.Serialization.Formatters.Binary;

namespace MarilynJIT.TuringML.Nodes
{
	public static class Serialization
	{
		
		
		private sealed class BeforeSerialize : IVisitor
		{
			public TuringNode Visit(TuringNode turingNode)
			{
				turingNode.VisitChildren(this);
				return turingNode.PrepareForSerialization();
			}
		}
		private sealed class AfterSerialize : IVisitor
		{
			public TuringNode Visit(TuringNode turingNode)
			{
				turingNode.VisitChildren(this);
				return turingNode.AfterDeserialization();
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
