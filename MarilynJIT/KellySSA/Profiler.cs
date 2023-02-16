using MarilynJIT.KellySSA.Nodes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using MarilynJIT.Util;
using System.Reflection;
using System.Collections.Concurrent;

namespace MarilynJIT.KellySSA.Profiler
{
	public sealed class LightweightBranchCounter : IProfilingCodeGenerator
	{
		private static void NotTaken(LightweightBranchCounter _this, ulong branch)
		{
			_this.counters[branch].nottaken += 1;
		}
		private static void Taken(LightweightBranchCounter _this, ulong branch)
		{
			_this.counters[branch].taken += 1;
		}
		private static readonly MethodInfo notTakenMethod = new Action<LightweightBranchCounter, ulong>(NotTaken).Method;
		private static readonly MethodInfo takenMethod = new Action<LightweightBranchCounter, ulong>(Taken).Method;
		private sealed class Counter
		{
			public ulong taken;
			public ulong nottaken;
		}
		private readonly Dictionary<ulong, Counter> counters = new Dictionary<ulong, Counter>();
		private ulong ctr;
		private readonly Dictionary<Conditional, ulong> knownNodes = new Dictionary<Conditional, ulong>(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<ulong, Conditional> knownNodesREV = new Dictionary<ulong, Conditional>();
		public void Generate(Conditional conditional, Expression getme, out Expression taken, out Expression notTaken)
		{
			if (!knownNodes.TryGetValue(conditional, out ulong id))
			{
				id = ctr++;
				knownNodes.Add(conditional, id);
				knownNodesREV.Add(id, conditional);
				counters.Add(id, new Counter());
			}
			Expression constant = Expression.Constant(id, typeof(ulong));
			taken = Expression.Call(takenMethod, getme, constant);
			notTaken = Expression.Call(notTakenMethod, getme, constant);
		}
		public void Strip(Node[] nodes, ushort offset, ulong minInvocations, bool emitBailout)
		{
			Dictionary<Conditional, bool> taken = new Dictionary<Conditional, bool>(ReferenceEqualityComparer.Instance);
			Dictionary<Conditional, bool> notTaken = new Dictionary<Conditional, bool>(ReferenceEqualityComparer.Instance);
			foreach (KeyValuePair<ulong, Counter> keyValuePair in counters)
			{
				Counter counter = keyValuePair.Value;
				Conditional conditional = null;
				if (counter.taken > minInvocations)
				{
					conditional = knownNodesREV[keyValuePair.Key];
					taken.Add(conditional, false);
				}
				if (counter.nottaken > minInvocations)
				{
					if (conditional is null)
					{
						conditional = knownNodesREV[keyValuePair.Key];
					}
					notTaken.Add(conditional, false);
				}
			}
			JITCompiler.PruneUnreached(nodes, offset, taken, notTaken, emitBailout);
		}
	}
}
