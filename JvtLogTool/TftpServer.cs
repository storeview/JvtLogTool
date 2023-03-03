﻿using System;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.IO;

namespace JvtLogTool
{
    class TftpServerRunner
    {
        public static Boolean Status = false;

        // TFTP Listens on port 69 by default for incoming connections to the server
        public static readonly int DEFAULT_PORT = 69;

        public static void createTftpServer(MainWindow mw)
        {
            mw.PrintSoftwareLog(false,"Simple TFTP Server");
            mw.PrintSoftwareLog(false,"By Joseph Pacura");
            mw.PrintSoftwareLog(false,"2018-01-19");
            mw.PrintSoftwareLog(false,"------------------");
            mw.PrintSoftwareLog(false,"Starting incoming connection listener on port " + DEFAULT_PORT);

            // Create a UDP client to listen for incoming connections
            UdpClient listener = null;
            try
            {
                listener = new UdpClient(DEFAULT_PORT);
                mw.PrintSoftwareLog("【Tftp】服务开启成功");
                TftpServerRunner.Status = true;
            }
            catch (SocketException e)
            {
                // There was an error creating the socket
                // This usually means that the port is already in use or is blocked
                mw.PrintSoftwareLog(false,"ERROR: A socket error has occurred!");
                mw.PrintSoftwareLog(false,"ERROR: This usually means that the selected port is already in use!");
                mw.PrintSoftwareLog(false,"ERROR: Server is shutting down");
                mw.PrintSoftwareLog("【Tftp】服务器开始失败，请检查UDP的69端口是否被占用");
                return;
            }

            while (true)
            {
                // Create an endpoint for the packet sender
                IPEndPoint sender = null;

                // Wait for a UDP Datagram to come in
                byte[] packet = listener.Receive(ref sender);

                // Get the IP Address and Port of the sender
                IPAddress sender_ip = sender.Address;
                int sender_port = sender.Port;

                mw.PrintSoftwareLog(false,"Received incoming packet from " + sender_ip + " on port " + sender_port);

                // Check the opcode on the received packet
                // Only RRQ and WRQ should come to the listening port
                int opcode = TFTP.getOpcodeFromHeader(packet);

                if (opcode != TFTP.OP_RRQ && opcode != TFTP.OP_WRQ)
                {
                    // An invalid TFTP packet has been received
                    mw.PrintSoftwareLog(false,"ERROR: Received packet has an invalid opcode");
                    mw.PrintSoftwareLog(false,"ERROR: Dropping packet");
                    continue;
                }

                if (opcode == TFTP.OP_RRQ)
                {
                    // A read request was received
                    string file = TFTP.getFileNameFromHeader(packet);
                    string mode = TFTP.getModeFromHeader(packet);

                    mw.PrintSoftwareLog(false,"Read request received for file " + file);
                    mw.PrintSoftwareLog(false,"Starting RRQ Thread");
                    mw.PrintSoftwareLog(false,"");

                    // This simple server does not support MAIL mode or NETASCII mode
                    if (mode == TFTP.MODE_MAIL || mode == TFTP.MODE_NETASCII)
                    {
                        string errmsg = "Server does not support " + mode + " mode!";
                        byte[] error = TFTP.encodeErrorHeader(4, errmsg);

                        mw.PrintSoftwareLog(false,errmsg);
                        mw.PrintSoftwareLog(false,"Terminating connection");
                        listener.Send(error, error.Length, sender);
                        continue;
                    }

                    // Create a separate thread for this request
                    Thread newThread = new Thread(() => RRQThreadMethod(listener, sender, file, mode, mw));
                    newThread.Start();
                }
                else if (opcode == TFTP.OP_WRQ)
                {
                    // A write request was received
                    string file = TFTP.getFileNameFromHeader(packet);
                    string mode = TFTP.getModeFromHeader(packet);

                    mw.PrintSoftwareLog(false,"Write request received for file " + file);
                    mw.PrintSoftwareLog(false,"Starting WRQ Thread");
                    mw.PrintSoftwareLog(false,"");

                    // This simple server does not support MAIL mode or NETASCII mode
                    if (mode == TFTP.MODE_MAIL || mode == TFTP.MODE_NETASCII)
                    {
                        string errmsg = "Server does not support " + mode + " mode!";
                        byte[] error = TFTP.encodeErrorHeader(4, errmsg);

                        mw.PrintSoftwareLog(false,errmsg);
                        mw.PrintSoftwareLog(false,"Terminating connection");
                        listener.Send(error, error.Length, sender);
                        continue;
                    }

                    // Create a separate thread for this request
                    Thread newThread = new Thread(() => WRQThreadMethod(listener, sender, file, mode, mw));
                    newThread.Start();
                }
            }
        }

        // Method for read request thread
        public static void RRQThreadMethod(UdpClient listener, IPEndPoint dest, string fileName, string mode, MainWindow mw)
        {
            mw.PrintSoftwareLog("【Tftp】准备发送一个文件："+ fileName); 
            mw.PrintSoftwareLog(false,"Read request thread started");

            // In order to send a file, we have to make sure it exists
            if (!File.Exists(fileName))
            {
                // The specified file does not exist
                mw.PrintSoftwareLog(false,"ERROR: The requested file does not exist");

                string errmsg = "The requested file " + fileName + " does not exist";
                byte[] error = TFTP.encodeErrorHeader(1, errmsg);
                listener.Send(error, error.Length, dest);

                mw.PrintSoftwareLog(false,"ERROR: Terminating Transfer");
                mw.PrintSoftwareLog(false,"");
                return;
            }

            // The file exists, select a random port for the transfer
            mw.PrintSoftwareLog(false,"Requested file exists");

            int sendport = -1;
            UdpClient sender = null;

            do
            {
                // Randomly select an outgoing port.
                // If the outgoing port is already taken, select another port

                sendport = TFTP.getRandomPort();

                try
                {
                    sender = new UdpClient(sendport);
                }
                catch (SocketException e)
                {
                    sendport = -1;
                    sender = null;
                }
            }
            while (sendport == -1);

            mw.PrintSoftwareLog(false,"Outgoing port " + sendport + " selected");

            // Create a new file stream to read the file
            FileStream fs = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            long fileLength = fs.Length;

            // If the file size is exactly a multiple of 512 bytes, we need to add an extra empty data packet at the end
            bool addExtraPacket = (fileLength % 512 == 0);
            long numParts = (long)Math.Ceiling((double)fileLength / 512.0);

            for (ushort i = 1; i <= numParts; i++)
            {
                // Read 512 bytes from stream into an array
                byte[] temp = new byte[512];
                int read_bytes = fs.Read(temp, 0, 512);

                // We are using a second array to prevent accidently adding extra bytes if less than 512
                byte[] data = new byte[read_bytes];
                Buffer.BlockCopy(temp, 0, data, 0, data.Length);

                // Now we need to wait for the client to ack the data packet
                while (true)
                {
                    IPEndPoint incomingEP = null;
                    byte[] incomingPacket;
                    if (sender.Available > 0)
                    {
                        incomingPacket = sender.Receive(ref incomingEP);
                    }
                    else
                    {
                        byte[] packet = TFTP.encodeDataHeader(i, data);
                        sender.Send(packet, packet.Length, dest);
                        mw.PrintSoftwareLog(false,"Sent Data Packet " + i + " of " + numParts);
                        Thread.Sleep(5);
                        continue;
                    }

                    int opcode = TFTP.getOpcodeFromHeader(incomingPacket);

                    if (opcode == TFTP.OP_ACK)
                    {
                        // The packet is an ACK packet
                        int blockNum = TFTP.getBlockNumberFromHeader(incomingPacket);

                        if (blockNum == i)
                        {
                            // ACK number matches packet
                            mw.PrintSoftwareLog(false,"Received ACK for Packet " + blockNum);
                            break;
                        }
                    }
                }
            }

            // If the data size is exactly a multiple of 512 bytes, we need to send an empty data packet
            // To indicate that the file is finished
            if (addExtraPacket)
            {
                // Now we need to wait for the client to ack the data packet
                while (true)
                {
                    IPEndPoint incomingEP = null;
                    byte[] incomingPacket;
                    if (sender.Available > 0)
                    {
                        incomingPacket = sender.Receive(ref incomingEP);
                    }
                    else
                    {
                        byte[] packet = TFTP.encodeDataHeader((ushort)(numParts + 1), new byte[0]);
                        sender.Send(packet, packet.Length, dest);
                        mw.PrintSoftwareLog(false,"Sent empty data packet to finish");
                        Thread.Sleep(5);
                        continue;
                    }

                    int opcode = TFTP.getOpcodeFromHeader(incomingPacket);

                    if (opcode == TFTP.OP_ACK)
                    {
                        // The packet is an ACK packet
                        int blockNum = TFTP.getBlockNumberFromHeader(incomingPacket);

                        if (blockNum == (numParts + 1))
                        {
                            // ACK number matches packet
                            mw.PrintSoftwareLog(false,"Received ACK for Packet " + blockNum);
                            break;
                        }
                    }
                }
            }

            // Close the file we read from
            fs.Close();

            mw.PrintSoftwareLog("【Tftp】文件发送成功");
            mw.PrintSoftwareLog(false,"File Sent Successfully!");
            mw.PrintSoftwareLog(false,"Read request thread terminated");
            mw.PrintSoftwareLog(false,"");
        }

        // Method for write request thread
        public static void WRQThreadMethod(UdpClient listener, IPEndPoint dest, string fileName, string mode, MainWindow mw)
        {
            mw.PrintSoftwareLog("【Tftp】准备接收一个新的文件：" + fileName);
            mw.PrintSoftwareLog(false,"Write request thread started");

            // In order to receive a file, we have to make sure it does not exist
            if (File.Exists(fileName))
            {
                // The specified file already exists
                mw.PrintSoftwareLog(false,"ERROR: The requested file already exists");

                string errmsg = "The requested file " + fileName + " already exists";
                byte[] error = TFTP.encodeErrorHeader(6, errmsg);
                listener.Send(error, error.Length, dest);

                mw.PrintSoftwareLog(false,"ERROR: Terminating Transfer");
                mw.PrintSoftwareLog(false,"");
                return;
            }

            // The file does not exist, select a random port for the transfer
            mw.PrintSoftwareLog(false,"Requested file does not exist");

            int sendport = -1;
            UdpClient sender = null;

            do
            {
                // Randomly select an outgoing port.
                // If the outgoing port is already taken, select another port

                sendport = TFTP.getRandomPort();

                try
                {
                    sender = new UdpClient(sendport);
                }
                catch (SocketException e)
                {
                    sendport = -1;
                    sender = null;
                }
            }
            while (sendport == -1);

            mw.PrintSoftwareLog(false,"Outgoing port " + sendport + " selected");

            // Send ack packet from new outgoing port
            byte[] ack = TFTP.encodeAckHeader(0);
            sender.Send(ack, ack.Length, dest);
            mw.PrintSoftwareLog(false,"Sent ACK packet");

            // Create a new file stream to write the file
            FileStream fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None);

            // Data will be received in an inifinite loop that will be terminated by the last packet
            bool done = false;

            // Current packet counter
            int counter = 1;
            while (!done)
            {
                // We also need a loop in case the packet does not get here properly
                while (true)
                {
                    // Receive a packet from the client
                    IPEndPoint incomingEP = null;
                    byte[] incomingPacket;

                    if (sender.Available > 0)
                    {
                        incomingPacket = sender.Receive(ref incomingEP);
                    }
                    else
                    {
                        if (counter > 1)
                        {
                            ack = TFTP.encodeAckHeader((ushort)(counter - 1));
                            sender.Send(ack, ack.Length, dest);
                            mw.PrintSoftwareLog(false,"Sent ACK packet " + (counter - 1));
                            Thread.Sleep(20);
                        }
                        continue;
                    }


                    int opcode = TFTP.getOpcodeFromHeader(incomingPacket);

                    if (opcode == TFTP.OP_DATA)
                    {
                        // DATA Packet Received

                        int blockNum = TFTP.getBlockNumberFromHeader(incomingPacket);

                        if (blockNum == counter)
                        {
                            // Correct packet received
                            mw.PrintSoftwareLog(false,"Received data packet " + blockNum);

                            // Write data to local file
                            byte[] data = TFTP.getDataFromHeader(incomingPacket);
                            fs.Write(data, 0, data.Length);

                            if (data.Length < 512)
                            {
                                // This is the last packet
                                done = true;

                                // ACK the last packet
                                ack = TFTP.encodeAckHeader((ushort)counter);
                                sender.Send(ack, ack.Length, dest);
                                mw.PrintSoftwareLog(false,"Sent ACK packet " + counter);
                            }

                            counter = counter + 1;

                            break;
                        }
                    }
                }
            }

            // Close the file we wrote
            fs.Close();

            mw.PrintSoftwareLog("【Tftp】文件接收成功！");
            mw.PrintSoftwareLog(false,"File Received Successfully!");
            mw.PrintSoftwareLog(false,"Write request thread terminated");
            mw.PrintSoftwareLog(false,"");
        }
    }
}
