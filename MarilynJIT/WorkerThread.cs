using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Concurrent;

namespace MarilynJIT
{
	public sealed class WorkerThread
	{
		private sealed class WorkerTerminationException : Exception{
			
		}
		private static void ThrowWorkerTermination(){
			throw new WorkerTerminationException();
		}
		private readonly struct WorkerThreadTask{
			public readonly TaskCompletionSource taskCompletionSource;
			public readonly Action action;

			public WorkerThreadTask(TaskCompletionSource taskCompletionSource, Action action)
			{
				this.taskCompletionSource = taskCompletionSource;
				this.action = action ?? throw new ArgumentNullException(nameof(action));
			}
		}
		public Task EnqueueWork(Action action){
			TaskCompletionSource taskCompletionSource = new();
			blockingCollection.Add(new WorkerThreadTask(taskCompletionSource, action));
			return taskCompletionSource.Task;
		}
		private readonly BlockingCollection<WorkerThreadTask> blockingCollection = new BlockingCollection<WorkerThreadTask>();
		private static void Executioner(object obj)
		{
			BlockingCollection<WorkerThreadTask> blockingCollection = (BlockingCollection<WorkerThreadTask>)obj;
			while (true){
				WorkerThreadTask workerThreadTask = blockingCollection.Take();
				try
				{
					workerThreadTask.action();
				}
				catch (WorkerTerminationException)
				{
					blockingCollection.CompleteAdding();
					while(blockingCollection.TryTake(out WorkerThreadTask workerThreadTask1)){
						workerThreadTask1.taskCompletionSource.SetCanceled();
					}
					blockingCollection.Dispose();
					return;
				} catch(Exception e){
					workerThreadTask.taskCompletionSource.SetException(e);
					continue;
				}
				workerThreadTask.taskCompletionSource.SetResult();
			}
		}
		~WorkerThread(){
			blockingCollection.Add(new WorkerThreadTask(null, ThrowWorkerTermination));
		}
		public WorkerThread(){
			Thread thread = new Thread(Executioner);
			thread.IsBackground = true;
			thread.Name = "MarilynJIT worker thread";
			thread.Start(blockingCollection);
		}
	}
}
