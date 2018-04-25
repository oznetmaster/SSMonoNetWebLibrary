//
// System.Net.FtpAsyncResult.cs
//
// Authors:
//	Carlos Alberto Cortez (calberto.cortez@gmail.com)
//
// (c) Copyright 2006 Novell, Inc. (http://www.novell.com)
// (c) Copyright 2018 Nivloc Enterprises Ltd
//

using System;
#if SSHARP
using Crestron.SimplSharp;
using Crestron.SimplSharp.CrestronIO;
using IAsyncResult = Crestron.SimplSharp.CrestronIO.IAsyncResult;
using AsyncCallback = Crestron.SimplSharp.CrestronIO.AsyncCallback;
#else
using System.IO;
using System.Threading;
using System.Net;
#endif

#if SSHARP
namespace SSMono.Net
#else
namespace System.Net
#endif
	{
	internal class FtpAsyncResult : IAsyncResult
		{
		private FtpWebResponse response;
		private ManualResetEvent waitHandle;
		private Exception exception;
		private AsyncCallback callback;
		private Stream stream;
		private object state;
		private bool completed;
		private bool synch;
		private object locker = new object ();

		public FtpAsyncResult (AsyncCallback callback, object state)
			{
			this.callback = callback;
			this.state = state;
			}

		public object AsyncState
			{
			get { return state; }
			}

		public bool CompletedSynchronously
			{
			get { return synch; }
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

		internal bool GotException
			{
			get { return exception != null; }
			}

		internal Exception Exception
			{
			get { return exception; }
			}

		internal FtpWebResponse Response
			{
			get { return response; }
			set { response = value; }
			}

		internal Stream Stream
			{
			get { return stream; }

			set { stream = value; }
			}

		internal void WaitUntilComplete ()
			{
			if (IsCompleted)
				return;

			AsyncWaitHandle.WaitOne ();
			}

		internal bool WaitUntilComplete (int timeout, bool exitContext)
			{
			if (IsCompleted)
				return true;

			return AsyncWaitHandle.WaitOne (timeout, exitContext);
			}

		internal void SetCompleted (bool synch, Exception exc, FtpWebResponse response)
			{
			this.synch = synch;
			this.exception = exc;
			this.response = response;
			lock (locker)
				{
				completed = true;
				if (waitHandle != null)
					waitHandle.Set ();
				}
			DoCallback ();
			}

		internal void SetCompleted (bool synch, FtpWebResponse response)
			{
			SetCompleted (synch, null, response);
			}

		internal void SetCompleted (bool synch, Exception exc)
			{
			SetCompleted (synch, exc, null);
			}

		internal void DoCallback ()
			{
			if (callback != null)
				{
				try
					{
					callback (this);
					}
				catch (Exception)
					{
					}
				}
			}

		// Cleanup resources
		internal void Reset ()
			{
			exception = null;
			synch = false;
			response = null;
			state = null;

			lock (locker)
				{
				completed = false;
				if (waitHandle != null)
					waitHandle.Reset ();
				}
			}

		#region IAsyncResult Members

#if SSHARP

		public object InnerObject
			{
			get { throw new NotImplementedException (); }
			}

		public CEventHandle AsyncWaitHandle
			{
			get
				{
				lock (locker)
					{
					if (waitHandle == null)
						waitHandle = new ManualResetEvent (false);
					}

				return waitHandle;
				}
			}
#else
		public WaitHandle AsyncWaitHandle
			{
			get
				{
				lock (locker)
					{
					if (waitHandle == null)
						waitHandle = new ManualResetEvent (false);
					}

				return waitHandle;
				}
			}
#endif


		#endregion
		}
	}