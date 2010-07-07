﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using SuperSocket.Common;
using SuperSocket.SocketServiceCore.AsyncSocket;

namespace SuperSocket.SocketServiceCore
{
    public class AsyncSocketServer<TSocketSession, TAppSession> : SocketServerBase<TSocketSession, TAppSession>, IAsyncRunner
        where TAppSession : IAppSession, new()
        where TSocketSession : ISocketSession<TAppSession>, IAsyncSocketSession, new()     
	{
        public AsyncSocketServer(IAppServer<TAppSession> appServer, IPEndPoint localEndPoint)
            : base(appServer, localEndPoint)
		{
            
		}

        private ManualResetEvent m_TcpClientConnected = new ManualResetEvent(false);

        private BufferManager m_ReceiveBufferManager;

        private BufferManager m_SendBufferManager;

        private SocketAsyncEventArgsPool m_ReadWritePool;

        private Semaphore m_MaxConnectionSemaphore;

        private Socket m_ListenSocket = null;

        private Thread m_ListenThread = null;

        private bool m_Stopped = false;

        public override bool Start()
		{
			try
			{
                if (!base.Start())
                    return false;

                m_ReceiveBufferManager = new BufferManager(AppServer.Config.ReceiveBufferSize * AppServer.Config.MaxConnectionNumber,
                    AppServer.Config.ReceiveBufferSize);
                m_ReceiveBufferManager.InitBuffer();

                m_SendBufferManager = new BufferManager(AppServer.Config.ReceiveBufferSize * AppServer.Config.MaxConnectionNumber, AppServer.Config.ReceiveBufferSize);
                m_SendBufferManager.InitBuffer();

                m_ReadWritePool = new SocketAsyncEventArgsPool(AppServer.Config.MaxConnectionNumber);

                // preallocate pool of SocketAsyncEventArgs objects
                SocketAsyncEventArgs receiveEventArg;
                SocketAsyncEventArgs sendEventArg;

                for (int i = 0; i < AppServer.Config.MaxConnectionNumber; i++)
                {
                    //Pre-allocate a set of reusable SocketAsyncEventArgs
                    receiveEventArg = new SocketAsyncEventArgs();
                    receiveEventArg.UserToken = new AsyncUserToken();
                    m_ReceiveBufferManager.SetBuffer(receiveEventArg);

                    sendEventArg = new SocketAsyncEventArgs();
                    sendEventArg.UserToken = new AsyncUserToken();
                    m_SendBufferManager.SetBuffer(sendEventArg);

                    // add SocketAsyncEventArg to the pool
                    m_ReadWritePool.Push(new SocketAsyncEventArgsProxy(receiveEventArg, sendEventArg));
                }

                if (m_ListenSocket == null)
                {
                    m_ListenThread = new Thread(StartListen);
                    m_ListenThread.Start();
                }

                return true;				
			}
			catch (Exception e)
			{
				LogUtil.LogError(e);
				return false;
			}			
		}        

        private void StartListen()
        {
            m_ListenSocket = new Socket(this.EndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            m_ListenSocket.Bind(this.EndPoint);
            m_ListenSocket.Listen(100);

            m_ListenSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            m_MaxConnectionSemaphore = new Semaphore(this.AppServer.Config.MaxConnectionNumber, this.AppServer.Config.MaxConnectionNumber);

            SocketAsyncEventArgs acceptEventArg = new SocketAsyncEventArgs();
            acceptEventArg.Completed += new EventHandler<SocketAsyncEventArgs>(acceptEventArg_Completed);

            while (!m_Stopped)
            {
                m_TcpClientConnected.Reset();
                acceptEventArg.AcceptSocket = null;
                if (!m_ListenSocket.AcceptAsync(acceptEventArg))
                    this.ExecuteAsync(AsyncRunType.ThreadPool, "AcceptNewClient", w => AceptNewClient(acceptEventArg));
                m_TcpClientConnected.WaitOne();
                m_MaxConnectionSemaphore.WaitOne();//two wait one here?
            }
        }

        void acceptEventArg_Completed(object sender, SocketAsyncEventArgs e)
        {
            AceptNewClient(e);
        }

        void AceptNewClient(SocketAsyncEventArgs e)
        {
            //Get the socket for the accepted client connection and put it into the 
            //ReadEventArg object user token
            SocketAsyncEventArgsProxy socketEventArgsProxy = m_ReadWritePool.Pop();
            socketEventArgsProxy.Socket = e.AcceptSocket;

            TSocketSession session = RegisterSession(e.AcceptSocket);
            session.SocketAsyncProxy = socketEventArgsProxy;
            session.Closed += new EventHandler<SocketSessionClosedEventArgs>(session_Closed);
            m_TcpClientConnected.Set();
            session.Start();
        }

		public void OnClientConnect(IAsyncResult result)
		{
            Socket clientSocket = null;

            try
            {
                Socket listener = result.AsyncState as Socket;

                if (listener == null)
                    return;

                clientSocket = listener.EndAccept(result);
            }
            catch (ObjectDisposedException)//listener has been stopped
            {
                m_TcpClientConnected.Set();
                return;
            }
            catch (Exception e)
            {
                LogUtil.LogError("Failed to accept new tcp client in async server!", e);
                m_TcpClientConnected.Set();
                return;
            }

            TSocketSession session = RegisterSession(clientSocket);
            session.Closed += new EventHandler<SocketSessionClosedEventArgs>(session_Closed);
            m_TcpClientConnected.Set();
			session.Start();
		}

        void session_Closed(object sender, SocketSessionClosedEventArgs e)
        {
            m_MaxConnectionSemaphore.Release();
            
            IAsyncSocketSession socketSession = sender as IAsyncSocketSession;
            if (socketSession != null)
                this.m_ReadWritePool.Push(socketSession.SocketAsyncProxy);
        }

		public override void Stop()
		{
			base.Stop();            

            if (m_ListenSocket != null)
			{
                m_ListenSocket.Close();
                m_ListenSocket = null;
			}

            m_Stopped = true;
		}
	}
}
