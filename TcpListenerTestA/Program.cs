using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpListenerTestA
{
    class Program
    {
        static void Main(string[] args)
        {
            var p = new Program();
            p.startsvc();
            Console.ReadLine();
        }
        public void startsvc()
        {
            //RouttingSession._Flag_tpktHeader=

            var routersvcport = new IPEndPoint(IPAddress.Any, AppSettings.Default.RouterSvcPort);
            TcpListener router = new TcpListener(routersvcport);

            router.Start(2);
            router.BeginAcceptSocket(new AsyncCallback(OnAccept), router);

        }

        public void OnAccept(IAsyncResult ar)
        {
            var router = ar.AsyncState as TcpListener;
            var client = router.EndAcceptSocket(ar);
            var rs = new RouttingSession();

            rs.StartRoutting(client);
            router.BeginAcceptSocket(OnAccept, router);
        }
    }
    public class RouttingSession
    {
        byte[] _Flag_tpktHeader = { 0x03, 0x00, 0x00 };
        int _Flag_routingTokenIndex = 11;//4+4+3 
        string _Flag_RouterTokenProtocal = "VSKC://";

        bool _Flag_FirstRoutting = true;


        public Socket ServerRouter, ClientRouter;
        public void StartRoutting(Socket client)
        {
            ClientRouter = client;

            StartClientRouter();
        }

        private void StartClientRouter()
        {
            SocketAsyncEventArgs clirearg = new SocketAsyncEventArgs();
            clirearg.SetBuffer(new byte[1024], 0, 1024);
            clirearg.RemoteEndPoint = ClientRouter.RemoteEndPoint;
            clirearg.UserToken = ClientRouter;
            clirearg.Completed += clirearg_Completed;
            ClientRouter.ReceiveAsync(clirearg);
        }
        void StartServerRouter()
        {
            SocketAsyncEventArgs svcrearg = new SocketAsyncEventArgs();
            svcrearg.SetBuffer(new byte[1024], 0, 1024);
            svcrearg.RemoteEndPoint = ServerRouter.RemoteEndPoint;
            svcrearg.UserToken = ServerRouter;
            svcrearg.Completed += svcrearg_Completed;
            ServerRouter.ReceiveAsync(svcrearg);

        }
        private void Forwarding(Socket src, Socket dest, SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred == 0) return;

            if (!ValidateState(dest, SelectMode.SelectWrite)) return;
            SocketError errorCode = SocketError.Success;
            dest.Send(e.Buffer, e.Offset, e.BytesTransferred, SocketFlags.None, out errorCode);
            if (errorCode != SocketError.Success) Close();
            try
            {
                src.ReceiveAsync(e);
            }
            catch
            {
                Close();
            }
        }

        void svcrearg_Completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred > 0)
                Forwarding(ServerRouter, ClientRouter, e);

        }

        void clirearg_Completed(object sender, SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred > 0)
            {
                if (_Flag_FirstRoutting)
                {
                    _Flag_FirstRoutting = false;
#if DEBUG
                    //Console.WriteLine("{0}---->{1}:[{2}]", src.RemoteEndPoint,dest.LocalEndPoint,e.BytesTransferred);

                    {
                        Console.WriteLine(BitConverter.ToString(e.Buffer, 0, e.BytesTransferred));
                        Console.WriteLine(System.Text.ASCIIEncoding.ASCII.GetString(e.Buffer, 0, e.BytesTransferred));
                        Console.WriteLine("=======================================");
                    }
#endif
                    //TODO:正式版使用RouterSessionKeyParser类实现
                    if (!TryStartServerRouter(e))
                    {
                        Close();
                    }
                }
                if (null != ServerRouter)
                {
                    Forwarding(ClientRouter, ServerRouter, e);
                }
            }
        }
        #region TryStartServerRouter
        private bool TryStartServerRouter(SocketAsyncEventArgs e)
        {
            if (e.Buffer.Take(_Flag_tpktHeader.Length).SequenceEqual(_Flag_tpktHeader))
            {
                var routerToken = System.Text.Encoding.ASCII.GetString(e.Buffer, _Flag_routingTokenIndex, Array.IndexOf<byte>(e.Buffer, 0x0D, _Flag_routingTokenIndex) - _Flag_routingTokenIndex);
                if (routerToken.StartsWith(_Flag_RouterTokenProtocal, StringComparison.CurrentCultureIgnoreCase))
                {
                    CreateServerRouterFromRouterToken(routerToken);
                    StartServerRouter();
                    return true;
                }
            }
            return false;
        }

        private void CreateServerRouterFromRouterToken(string routerToken)
        {
            var rska = routerToken.Substring(_Flag_RouterTokenProtocal.Length).Trim().Split(':');

            var svcep = new IPEndPoint(IPAddress.Parse(rska[0]), rska.Length > 1 ? int.Parse(rska[1]) : 3389);
            var svc = new TcpClient().Client;
            svc.Connect(svcep);
            ServerRouter = svc;

        }
        #endregion

        private bool ValidateState(Socket socket, params SelectMode[] selectModes)
        {
            if (!socket.Connected)
            {
                Close();
                return false;
            }
            try
            {
                foreach (var selectMode in selectModes)
                {
                    var ret = socket.Poll(1000 * 500, selectMode);
                    if (!ret)
                    {
                        Close();
                        return false;
                    }
                }
            }
            catch
            {
                Close();
                return false;
            }
            return true;
        }

        private void Close()
        {
            try
            {
                ClientRouter.Close();
            }
            catch { }
            try
            {
                ServerRouter.Close();
            }
            catch { }
        }
    }

}
