using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MarilynJIT.TuringML
{
	public sealed class VirtualMemoryManager
	{
		private readonly ConcurrentBag<double[]> mempool;
		private readonly int maxpages;
		private readonly Dictionary<double, double[]> dictionary = new Dictionary<double, double[]>();

		public VirtualMemoryManager(ConcurrentBag<double[]> mempool, int maxpages)
		{
			this.mempool = mempool ?? throw new ArgumentNullException(nameof(mempool));
			this.maxpages = maxpages;
		}

		public double Read(double address){
			if (double.IsNaN(address) | double.IsInfinity(address))
			{
				return double.NaN;
			}
			if(dictionary.TryGetValue(Math.Floor(address / 4096), out double[] array)){
				int index = (int)Math.Floor(address % 4096);
				if (index < 0)
				{
					index = 4095 - index;
				}
				return array[index];
			}
			return 0;
		}
		public void Write(double address, double value){
			if (double.IsNaN(address) | double.IsInfinity(address))
			{
				return;
			}
			double pageid = Math.Floor(address / 4096);
			if (!dictionary.TryGetValue(pageid, out double[] array))
			{
				if(dictionary.Count < maxpages){
					if(!mempool.TryTake(out array)){
						array = new double[4096];
					}
				} else{
					//Bailout since we exceeded memory limit
					throw new AIBailoutException();
				}
			}
			int index = (int)Math.Floor(address % 4096);
			if(index < 0){
				index = 4095 - index;
			}
			array[index] = value;
		}

		private static void Free(VirtualMemoryManager virtualMemoryManager){
			foreach(double[] array in virtualMemoryManager.dictionary.Values){
				Array.Clear(array, 0, 4096);
				try{
					virtualMemoryManager.mempool.Add(array);
				} catch(ObjectDisposedException){
					return;
				}
			}
		}
		~VirtualMemoryManager(){
			ThreadPool.QueueUserWorkItem(Free, this, false);	
		}
	}
}
