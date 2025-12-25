using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ChessClient
{
    class Program
    {
        static void Main()
        {
            Console.Write("Enter server IP address (or press Enter for localhost): ");
            var serverIp = Console.ReadLine();
            if (string.IsNullOrEmpty(serverIp))
            {
                serverIp = "127.0.0.1";
            }

            Client client = new(serverIp, 19509);
            client.Connect();
        }
    }

    class Client
    {
        private TcpClient? tcpClient;
        private StreamReader? reader;
        private StreamWriter? writer;
        private readonly string serverIp;
        private readonly int port;
        private PieceColor myColor;
        private readonly List<List<Piece?>> board = [];

        public Client(string ip, int p)
        {
            serverIp = ip;
            port = p;
            InitializeBoard();
        }

        private void InitializeBoard()
        {
            for (int i = 0; i < 8; i++)
            {
                board.Add(new List<Piece?>());
                for (int j = 0; j < 8; j++)
                {
                    board[i].Add(null);
                }
            }
        }

        public void Connect()
        {
            try
            {
                tcpClient = new TcpClient(serverIp, port);
                reader = new StreamReader(tcpClient.GetStream(), Encoding.UTF8);
                writer = new StreamWriter(tcpClient.GetStream(), Encoding.UTF8) { AutoFlush = true };

                writer.WriteLine("CHESS_V1_START");

                Console.WriteLine("Connected to server. Waiting for opponent...");

                while (true)
                {
                    var message = ReceiveMessage();
                    if (string.IsNullOrEmpty(message))
                    {
                        Console.WriteLine("Disconnected from server");
                        break;
                    }

                    if (message.StartsWith("COLOR:"))
                    {
                        myColor = message.Substring(6) == "White" ? PieceColor.White : PieceColor.Black;
                        Console.WriteLine($"\nYou are playing as {myColor}");
                    }
                    else if (message.StartsWith("BOARD:"))
                    {
                        ParseBoard(message.Substring(6));
                        PrintBoard();
                    }
                    else if (message == "YOURTURN")
                    {
                        Console.WriteLine("\n>>> YOUR TURN <<<");
                        Console.Write("Enter move (e.g., e2 e4): ");
                        var move = Console.ReadLine();
                        SendMessage(move!);
                    }
                    else if (message == "WAIT")
                    {
                        Console.WriteLine("\nWaiting for opponent's move...");
                    }
                    else if (message.StartsWith("ERROR:"))
                    {
                        Console.WriteLine($"\n!!! {message.Substring(6)} !!!");
                    }
                    else if (message.StartsWith("WIN:"))
                    {
                        Console.WriteLine($"\n{'=',40}");
                        Console.WriteLine($"GAME OVER! {message.Substring(4)} wins!");
                        Console.WriteLine($"{'=',40}");
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                reader?.Close();
                writer?.Close();
                tcpClient?.Close();
            }
        }

        private void ParseBoard(string boardState)
        {
            var pieces = boardState.Split(',');
            int index = 0;

            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    if (index >= pieces.Length || pieces[index] == "_" || pieces[index] == "")
                    {
                        board[i][j] = null;
                    }
                    else
                    {
                        var pieceStr = pieces[index];
                        if (pieceStr.Length >= 2)
                        {
                            var color = (PieceColor)pieceStr[0];
                            var type = (PieceType)pieceStr[1];
                            board[i][j] = new Piece(color, type);
                        }
                        else
                        {
                            board[i][j] = null;
                        }
                    }
                    index++;
                }
            }
        }

        private void PrintBoard()
        {
            Console.WriteLine();

            if (myColor == PieceColor.White)
            {
                PrintBoardNormal();
            }
            else
            {
                PrintBoardInverted();
            }
        }

        private void PrintBoardNormal()
        {
            List<int> ranks = [8, 7, 6, 5, 4, 3, 2, 1];
            List<string> files = [" ", " A ", " B ", " C ", " D ", " E ", " F ", " G ", " H"];

            for (var i = 0; i < 8; i++)
            {
                Console.Write(ranks[i]);
                for (var j = 0; j < 8; j++)
                {
                    if (board[i][j] is not null)
                    {
                        Console.Write(board[i][j]?.Symbol);
                    }
                    else
                    {
                        Console.Write("   ");
                    }
                }
                Console.WriteLine();
            }
            files.ForEach(Console.Write);
            Console.WriteLine();
        }

        private void PrintBoardInverted()
        {
            List<int> ranks = [8, 7, 6, 5, 4, 3, 2, 1];
            List<string> files = [" ", " A ", " B ", " C ", " D ", " E ", " F ", " G ", " H"];

            for (var i = 7; i >= 0; i--)
            {
                Console.Write(ranks[i]);
                for (var j = 0; j < 8; j++)
                {
                    if (board[i][j] is not null)
                    {
                        Console.Write(board[i][j]?.Symbol);
                    }
                    else
                    {
                        Console.Write("   ");
                    }
                }
                Console.WriteLine();
            }
            files.ForEach(Console.Write);
            Console.WriteLine();
        }

        private void SendMessage(string message)
        {
            writer?.WriteLine(message);
        }

        private string ReceiveMessage()
        {
            return reader?.ReadLine() ?? "";
        }
    }

    internal enum PieceColor
    {
        White,
        Black
    }

    internal enum PieceType
    {
        Pawn,
        Rook,
        Knight,
        Bishop,
        King,
        Queen
    }

    internal class Piece
    {
        public readonly PieceColor Color;
        public readonly PieceType Type;

        public Piece(PieceColor color, PieceType type)
        {
            Color = color;
            Type = type;
        }

        public string Symbol => Type switch
        {
            PieceType.King => Color == PieceColor.Black ? " ♔ " : " ♚ ",
            PieceType.Queen => Color == PieceColor.Black ? " ♕ " : " ♛ ",
            PieceType.Rook => Color == PieceColor.Black ? " ♖ " : " ♜ ",
            PieceType.Pawn => Color == PieceColor.Black ? " ♙ " : " ♟ ",
            PieceType.Knight => Color == PieceColor.Black ? " ♘ " : " ♞ ",
            PieceType.Bishop => Color == PieceColor.Black ? " ♗ " : " ♝ ",
            _ => throw new ArgumentOutOfRangeException(nameof(Type), Type, null)
        };
    }
}