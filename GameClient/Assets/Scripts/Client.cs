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
using System.Threading;

public class Client : MonoBehaviour
{
    private static CancellationTokenSource cancellationTokenSource;
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
    private void OnDestroy()
    {
        cancellationTokenSource?.Cancel();
    }
    private void OnApplicationQuit()
    {
        Disconnect();
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

        private async void ConnectCallback(IAsyncResult _result)
        {
            socket.EndConnect(_result);

            if (!socket.Connected)
            {
                return;
            }
            cancellationTokenSource = new CancellationTokenSource();
            SslStream _sslStream = new SslStream(
               socket.GetStream(),
               false,
               new RemoteCertificateValidationCallback(ValidateServerCertificate),
               null
               );
            try
            {
                SslClientAuthenticationOptions sslClientAuthenticationOptions = new SslClientAuthenticationOptions();
                sslClientAuthenticationOptions.TargetHost=serverName;
                
                await _sslStream.AuthenticateAsClientAsync(sslClientAuthenticationOptions, cancellationTokenSource.Token);
                
            }
            catch (AuthenticationException e)
            {
                Debug.Log(e.Message);
                if (e.InnerException != null)
                {
                    Debug.Log(e.InnerException.Message);
                }
                //Console.WriteLine("Authentication failed - closing the connection.");
                socket.Close();
                return;
            }
            catch
            {
                Debug.Log("Task was cancelled!");
                return;
            }
            finally
            {
                cancellationTokenSource.Dispose();
                cancellationTokenSource = null;
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
                    Disconnect();
                    return;
                }

                byte[] _data = new byte[_byteLength];
                Array.Copy(receiveBuffer, _data, _byteLength);

                receivedData.Reset(HandleData(_data));
                sslStream.BeginRead(receiveBuffer, 0, dataBufferSize, ReceiveCallback, null);
            }
            catch
            {
                Disconnect();
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
        private void Disconnect()
        {
            sslStream?.Dispose();
            socket?.Close();
            sslStream = null;
            receivedData = null;
            receiveBuffer = null;
            socket = null;
            instance.Disconnect();
        }
    }
    private void Disconnect()
    {
        try
        {
            tcp.socket?.Close();
            Debug.Log("Disconnected From Server");
        }
        catch (Exception e)
        {

            Debug.Log($"Exception thrown {e}");
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
