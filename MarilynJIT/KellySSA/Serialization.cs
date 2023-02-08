using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace MarilynJIT.KellySSA.Nodes
{
	public static class Serialization
	{
		public static Node[] DeserializeJsonNodesArray(string json){
			JObject[] objects = JsonConvert.DeserializeObject<JObject[]>(json);
			ushort len = (ushort)objects.Length;
			Node[] nodes = new Node[len];
			for(ushort i = 0; i < len; ++i){
				JObject obj = objects[i];
				if(obj is null){
					continue;
				}
				nodes[i] = GetNode(obj);
			}
			return nodes;
		}
		private static Node GetNode(JObject obj){
			string nodetype = obj.GetValue("typeName").ToObject<string>();
			switch (nodetype)
			{
				case "ConstantNode":
					return new ConstantNode(obj.GetValue("value").ToObject<double>());
				case "AdditionOperator":
					return new AdditionOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "SubtractionOperator":
					return new SubtractionOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "MultiplyOperator":
					return new MultiplyOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "DivideOperator":
					return new DivideOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "ModuloOperator":
					return new ModuloOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "ExponentOperator":
					return new ExponentOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "LogOperator":
					return new LogOperator(obj.GetValue("first").ToObject<ushort>(), obj.GetValue("second").ToObject<ushort>());
				case "Move":
					return new Move(obj.GetValue("target").ToObject<ushort>());
				case "Conditional":
					return new Conditional(obj.GetValue("x").ToObject<ushort>(), obj.GetValue("y").ToObject<ushort>(), obj.GetValue("z").ToObject<ushort>());
				case "Bailout":
					return new Bailout(obj.GetValue("mybranch").ToObject<ushort>(), obj.GetValue("z").ToObject<ushort>(), obj.GetValue("taken").ToObject<bool>());
				case "ArgumentNode":
					return new ArgumentNode(obj.GetValue("parameterId").ToObject<ushort>());
				default:
					throw new Exception("Unrecognized node type: " + nodetype);
			}
		}


	}
}
