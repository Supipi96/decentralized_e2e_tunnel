﻿using System;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;  
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.IO;

namespace peer
{
    class Program
    {
        static string[] client_buffers;
        static Dictionary<int, IPEndPoint> client_map;
        static Dictionary<IPEndPoint, int> client_map_reverse;
        static int client_count;

        static X509Certificate2 server_cert;
        
        static void Main(string[] args)
        {
            client_buffers = new string[10];
            for(int i=0;i<10;++i)
            {
                client_buffers[i] = null;
            }

            client_map = new Dictionary<int, IPEndPoint>();
            client_map_reverse = new Dictionary<IPEndPoint, int>();
            client_count=0;

            server_cert = new X509Certificate2("/home/pasan/Documents/FYP_certificates/ssl-certificate.pfx", "password", X509KeyStorageFlags.PersistKeySet);
            X509Store store = new X509Store(StoreName.My);
            store.Open(OpenFlags.ReadWrite);
            store.Add(server_cert);

            int port = 27005;
            IPAddress localAddr = IPAddress.Parse("127.0.0.1");
            TcpListener server = new TcpListener(localAddr, port);

            server.Start();
            Console.WriteLine("Server is starting");
            Thread connections_t = new Thread(() => handle_connections(server));
            //handle_connections(server);
            connections_t.Start();
            handle_relay_requests();

        }

        public static void con_relay_listner(UdpClient client, IPEndPoint ip, int peer)
        {
            while(true)
            {
                string response = Encoding.UTF8.GetString(client.Receive(ref ip));
                Console.WriteLine("client"+peer+": "+ response);
                client_buffers[peer] = response;
            }

        }

        static void handle_connections(TcpListener server)
        {
            Console.WriteLine("Server is listening for clients to initialize a connection");
            Byte[] bytes = new Byte[256];
            string response;
            while(true)
            {
                TcpClient client = server.AcceptTcpClient();
                SslStream sslStream = new SslStream(client.GetStream(), false);
                sslStream.AuthenticateAsServer(server_cert, clientCertificateRequired: false,SslProtocols.Tls13, checkCertificateRevocation: true);

                sslStream.ReadTimeout = 5000;
                sslStream.WriteTimeout = 5000;
                // Read a message from the client.
                sslStream.Read(bytes, 0, bytes.Length);
                response = Encoding.UTF8.GetString(bytes);
                Console.WriteLine(response);

                Console.WriteLine(((IPEndPoint)client.Client.RemoteEndPoint)+" is requesting a connection");
                client_map[client_count] = (IPEndPoint)client.Client.RemoteEndPoint;
                client_map_reverse[(IPEndPoint)client.Client.RemoteEndPoint] = client_count++;

                Console.WriteLine("address "+ (client_count-1)+" is now reserved for client " + ((IPEndPoint)client.Client.RemoteEndPoint));

                byte[] message = Encoding.UTF8.GetBytes((client_count-1).ToString());
                sslStream.Write(message);

                sslStream.Close();
                client.Close();
            }
        }

        static void handle_relay_requests()
        {
            int port = 27005;
            while(true)
            {
                UdpClient udpListener = new UdpClient(port);

                IPEndPoint ipEndPoint = new IPEndPoint(IPAddress.Any, port);
                byte[] receivedBytes = udpListener.Receive(ref ipEndPoint);
                string message = Encoding.UTF8.GetString(receivedBytes);
                Console.WriteLine("ip: " + ipEndPoint);
                Console.WriteLine("message: " + message);
                int peer0 = client_map_reverse[ipEndPoint];
                int peer1 = Int32.Parse(message);
                Console.WriteLine("Client "+ peer0 + " requesting a connection to " + peer1);
                udpListener.Close();
                Thread p2p_connection_t = new Thread(() => init_relay(peer0, peer1));
                p2p_connection_t.Start();
            }
            

        }

        static void init_relay(int peer0, int peer1)
        {
            UdpClient client0 = new UdpClient();
            IPEndPoint peerIP0 = client_map[peer0];

            UdpClient client1 = new UdpClient();
            IPEndPoint peerIP1 = client_map[peer1];

            send_message_udp(client1, peerIP1, peer0.ToString());
            string response = Encoding.UTF8.GetString(client1.Receive(ref peerIP1));

            if(response == "ACCEPT"){
                Console.WriteLine("connection accepted");
                send_message_udp(client1, peerIP1, ("connected to "+peer0));
                send_message_udp(client0, peerIP0, ("connected to "+peer1));
                start_relay(peer0, peer1, client0, peerIP0, client1, peerIP1);
            }
            else if(response == "REJECT"){
                Console.WriteLine("connection rejected");
            }
        }
        static void send_message_udp(UdpClient client, IPEndPoint ip, String message)
        {
            byte[] data = Encoding.UTF8.GetBytes(message);
            client.Send(data, data.Length, ip);
        }

        static void start_relay(int peer0, int peer1, UdpClient client0, IPEndPoint ip0,UdpClient client1, IPEndPoint ip1)
        {
            Thread peer0_t = new Thread(() => con_relay_listner(client0, ip0, peer0));
            Thread peer1_t = new Thread(() => con_relay_listner(client1, ip1, peer1));
            peer0_t.Start();
            peer1_t.Start();

            while(true)
            {
                if(client_buffers[peer0] != null){
                    send_message_udp(client1, ip1,client_buffers[peer0]);
                    client_buffers[peer0] = null;
                }

                if(client_buffers[peer1] != null){
                    send_message_udp(client0, ip0,client_buffers[peer1]);
                    client_buffers[peer1] = null;
                }
            }
        }
    }
}
