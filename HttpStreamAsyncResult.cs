//
// System.Net.HttpStreamAsyncResult
//
// Authors:
//	Gonzalo Paniagua Javier (gonzalo@novell.com)
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
// Copyright (c) 2018 Nivloc Enterprises Ltd

//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
#if SSHARP
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;
using IAsyncResult = Crestron.SimplSharp.CrestronIO.IAsyncResult;
using AsyncCallback = Crestron.SimplSharp.CrestronIO.AsyncCallback;
#else
using System.Threading;
#endif

#if SSHARP
namespace SSMono.Net
#else
namespace System.Net
#endif
	{
	internal class HttpStreamAsyncResult : IAsyncResult
		{
		private object locker = new object ();
		private ManualResetEvent handle;
		private bool completed;

		internal byte[] Buffer;
		internal int Offset;
		internal int Count;
		internal AsyncCallback Callback;
		internal object State;
		internal int SynchRead;
		internal Exception Error;

		public void Complete (Exception e)
			{
			Error = e;
			Complete ();
			}

		public void Complete ()
			{
			lock (locker)
				{
				if (completed)
					return;

				completed = true;
				if (handle != null)
					handle.Set ();

				if (Callback != null)
#if SSHARP
					Callback.BeginInvokeEx (this, null, null);
#else
					Callback.BeginInvoke (this, null, null);
#endif
				}
			}

		public object AsyncState
			{
			get { return State; }
			}

		public bool CompletedSynchronously
			{
			get { return (SynchRead == Count); }
			}

		public bool IsCompleted
			{
			get
				{
				lock (locker)
					{
					return completed;
					}
				}
			}

		#region IAsyncResult Members

#if SSHARP
		CEventHandle IAsyncResult.AsyncWaitHandle
			{
			get
				{
				lock (locker)
					{
					if (handle == null)
						handle = new ManualResetEvent (completed);
					}

				return handle;
				}
			}

		public object InnerObject
			{
			get { throw new NotImplementedException (); }
			}
#else
		public WaitHandle AsyncWaitHandle
			{
			get
				{
				lock (locker)
					{
					if (handle == null)
						handle = new ManualResetEvent (completed);
					}

				return handle;
				}
			}
#endif

		#endregion
		}
	}