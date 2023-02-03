using MarilynJIT.KellySSA.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using MarilynJIT.Util;
using System.Reflection;

namespace MarilynJIT.KellySSA
{
	public sealed class BranchLivenessProfiler : IProfilingCodeGenerator, IDisposable
	{
		private readonly Dictionary<Conditional, ulong> knownNodes = new Dictionary<Conditional, ulong>(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<ulong, Conditional> knownNodesREV = new Dictionary<ulong, Conditional>();
		private readonly Dictionary<ulong, bool> notTaken = new Dictionary<ulong, bool>();
		private readonly Dictionary<ulong, bool> taken = new Dictionary<ulong, bool>();
		private ulong ctr;
		private readonly ulong me;
		private readonly Expression getme;
		public BranchLivenessProfiler(){
			me = RegisteredObjectsManager<BranchLivenessProfiler>.Add(this);
			getme = Expression.Call(RegisteredObjectsManager<BranchLivenessProfiler>.get, Expression.Constant(me, typeof(ulong)));
		}
		private static void NotTaken(BranchLivenessProfiler _this, ulong branch){
			_this.notTaken.TryAdd(branch, false);
		}
		private static void Taken(BranchLivenessProfiler _this, ulong branch)
		{
			_this.taken.TryAdd(branch, false);
		}
		private static readonly MethodInfo notTakenMethod = new Action<BranchLivenessProfiler, ulong>(NotTaken).Method;

		private static readonly MethodInfo takenMethod = new Action<BranchLivenessProfiler, ulong>(Taken).Method;
		public void Generate(Conditional conditional, out Expression taken, out Expression notTaken)
		{
			if(!knownNodes.TryGetValue(conditional, out ulong id)){
				id = ctr++;
				knownNodes.Add(conditional, id);
				knownNodesREV.Add(id, conditional);
			}
			Expression constant = Expression.Constant(id, typeof(ulong));
			taken = Expression.Call(takenMethod, getme, constant);
			notTaken = Expression.Call(notTakenMethod, getme, constant);
		}

		public void Dispose()
		{
			RegisteredObjectsManager<BranchLivenessProfiler>.Free(me);
		}
		public void Strip(Node[] nodes, ushort offset){
			Dictionary<Conditional, bool> taken2 = new Dictionary<Conditional, bool>();
			foreach(ulong v in taken.Keys){
				taken2.Add(knownNodesREV[v], false);
			}
			Dictionary<Conditional, bool> notTaken2 = new Dictionary<Conditional, bool>();
			foreach (ulong v in notTaken.Keys)
			{
				notTaken2.Add(knownNodesREV[v], false);
			}
			JITCompiler.PruneUnreached(nodes, offset, taken2, notTaken2);
		}
	}
}
