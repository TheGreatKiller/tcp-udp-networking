using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.IO;

public class Client : MonoBehaviour
{
    public static Client instance;
    public static int dataBufferSize = 4096;

    public string ip = "127.0.0.1";
    public int port = 26950;
    public int myId = 0;
    public TCP tcp;

    private delegate void PacketHandler(Packet _packet);
    private static Dictionary<int, PacketHandler> packetHandlers;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Debug.Log("Instance already exists, destroying object!");
            Destroy(this);
        }
    }

    private void Start()
    {
        tcp = new TCP();
    }

    public void ConnectToServer()
    {
        InitializeClientData();

        tcp.Connect();
    }

    public class TCP
    {
        public TcpClient socket;
        private SslStream sslStream;
        private NetworkStream stream;
        private Packet receivedData;
        private byte[] receiveBuffer;
        
        private string serverName = "127.0.0.1";
        public void Connect()
        {
            socket = new TcpClient
            {
                ReceiveBufferSize = dataBufferSize,
                SendBufferSize = dataBufferSize
            };

            receiveBuffer = new byte[dataBufferSize];
            socket.BeginConnect(instance.ip, instance.port, ConnectCallback, socket);
        }

        private void ConnectCallback(IAsyncResult _result)
        {
            socket.EndConnect(_result);

            if (!socket.Connected)
            {
                return;
            }
            
            SslStream _sslStream = new SslStream(
               socket.GetStream(),
               false,
               new RemoteCertificateValidationCallback(ValidateServerCertificate),
               null
               );
            // The server name must match the name on the server certificate.
            try
            {
                _sslStream.AuthenticateAsClient(serverName);
            }
            catch (AuthenticationException e)
            {
                Debug.Log(e.Message);
                if (e.InnerException != null)
                {
                    Debug.Log( e.InnerException.Message);
                }
                //Console.WriteLine("Authentication failed - closing the connection.");
                socket.Close();
                return;
            }
            sslStream = _sslStream;
            receivedData = new Packet();

            sslStream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
        }

        private bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            if (sslPolicyErrors == SslPolicyErrors.None)
            {
                // Optionally, check if the certificate meets your specific criteria
                // For example, validate the certificate's expiration date, subject, etc.

                // If all checks pass, return true to accept the certificate
                return true;
            }
            Debug.Log(certificate.Subject);
            Debug.Log(serverName);
            // If there are SSL policy errors, reject the certificate
            Debug.Log($"SSL Policy Errors: {sslPolicyErrors}");
            return false;
        }

        public void SendData(Packet _packet)
        {
            try
            {
                if (socket != null)
                {
                    sslStream.BeginWrite(_packet.ToArray(), 0, _packet.Length(), null, null);
                }
            }
            catch (Exception _ex)
            {
                Debug.Log($"Error sending data to server via TCP: {_ex}");
            }
        }

        private void ReceiveCallback(IAsyncResult _result)
        {
            try
            {
                int _byteLength = sslStream.EndRead(_result);
                if (_byteLength <= 0)
                {
                    // TODO: disconnect
                    return;
                }

                byte[] _data = new byte[_byteLength];
                Array.Copy(receiveBuffer, _data, _byteLength);
                string logData = string.Empty;
                for (int i = 0; i < _data.Length; i++)
                {
                    logData += _data[i];
                }
                Debug.Log(logData);
                receivedData.Reset(HandleData(_data));
                sslStream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
            }
            catch
            {
                // TODO: disconnect
            }
        }

        private bool HandleData(byte[] _data)
        {
            int _packetLength = 0;

            receivedData.SetBytes(_data);

            if (receivedData.UnreadLength() >= 4)
            {
                _packetLength = receivedData.ReadInt();
                if (_packetLength <= 0)
                {
                    return true;
                }
            }

            while (_packetLength > 0 && _packetLength <= receivedData.UnreadLength())
            {
                byte[] _packetBytes = receivedData.ReadBytes(_packetLength);
                ThreadManager.ExecuteOnMainThread(() =>
                {
                    using (Packet _packet = new Packet(_packetBytes))
                    {
                        int _packetId = _packet.ReadInt();
                        packetHandlers[_packetId](_packet);
                    }
                });

                _packetLength = 0;
                if (receivedData.UnreadLength() >= 4)
                {
                    _packetLength = receivedData.ReadInt();
                    if (_packetLength <= 0)
                    {
                        return true;
                    }
                }
            }

            if (_packetLength <= 1)
            {
                return true;
            }

            return false;
        }
    }

    private void InitializeClientData()
    {
        packetHandlers = new Dictionary<int, PacketHandler>()
        {
            { (int)ServerPackets.welcome, ClientHandle.Welcome }
        };
        Debug.Log("Initialized packets.");
    }
}
