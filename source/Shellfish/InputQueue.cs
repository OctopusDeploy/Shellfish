using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Octopus.Shellfish;

// If we just write directly to the process stdInput when we want to write a line,
// it may block or throw exceptions. Both the blocking and exception throwing could leak
// out to the caller and cause unexpected problems. To solve these, we queue input and process
// it asynchronously in the background
public class InputQueue : IInputSourceObserver
{
    readonly StreamWriter processStdInput;
    readonly Queue<Notification> queue = new();
    TaskCompletionSource<bool> wakeupSignal = new();

    public InputQueue(StreamWriter processStdInput)
    {
        this.processStdInput = processStdInput;
        _ = BeginMessagePump();
    }
    
    public void OnNext(string line)
    {
        TaskCompletionSource<bool> sig;
        lock(queue)
        {
            queue.Enqueue(new(NotificationType.Next, line));
            
            // it is very wasteful to allocate a new TaskCompletionSource for every line, but it's simple.
            // Shellfish only deals with a few lines of input so we can accept the compromise.
            sig = wakeupSignal; 
            wakeupSignal = new();
        }

        sig.TrySetResult(true);
    }

    public void OnCompleted()
    {
        TaskCompletionSource<bool> sig;
        lock(queue)
        {
            queue.Enqueue(new(NotificationType.Completed));
            sig = wakeupSignal;
            wakeupSignal = new();
        }

        sig.TrySetResult(true);
    }
    
    async Task BeginMessagePump()
    {
        while (true)
        {
            Notification notification;
            lock (queue)
            {
                notification = queue.Count > 0 ? queue.Dequeue() : default;
            }

            switch (notification.Type)
            {
                case NotificationType.Empty: //  wait for the next wakeup signal
                {
                    TaskCompletionSource<bool> sig;
                    lock (queue)
                    {
                        sig = wakeupSignal;
                    }
                    await sig.Task;
                    continue; // go round again
                }
                
                case NotificationType.Next when notification.Line is not null: // onNext
                    await processStdInput.WriteLineAsync(notification.Line);
                    await processStdInput.FlushAsync();
                    break;
                
                case NotificationType.Completed:
                    processStdInput.Close();
                    return; // exit the entire message pump

                // Normally we would have a default: case which logged or threw an "Unhandled case" exception,
                // but we are a background task, there's nobody to observe such a thing.
            }
        }
    }

    // the correct way to model this is with a Notification base-class and inherited Next/Completed classes,
    // but we only have two cases so we can use a struct to avoid allocations
    enum NotificationType { Empty, Next, Completed }
    record struct Notification(NotificationType Type, string? Line = null);
}