using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ChessServer
{
    class Program
    {
        static void Main()
        {
            Server server = new();
            server.Start();
        }
    }

    class Server
    {
        private TcpListener? listener;
        private Queue<TcpClient> waitingPlayers = new();
        private List<GameSession> games = new();

        public void Start()
        {
            int port = int.Parse(Environment.GetEnvironmentVariable("PORT") ?? "5000");
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Chess Server started on port {port}");

            while (true)
            {
                var client = listener.AcceptTcpClient();
                Task.Run(() => Gatekeeper(client));
            }
        }

        private async Task Gatekeeper(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 2000;

                using (var reader = new StreamReader(client.GetStream(), Encoding.UTF8, leaveOpen: true))
                {
                    // Wait for the secret line
                    string? handshake = await reader.ReadLineAsync();

                    if (handshake != null && handshake.Trim() == "CHESS_V1_START")
                    {
                        Console.WriteLine("Verified player joined.");

                        // ONLY NOW do we add them to the game queue
                        lock (waitingPlayers)
                        {
                            waitingPlayers.Enqueue(client);
                            if (waitingPlayers.Count >= 2)
                            {
                                var white = waitingPlayers.Dequeue();
                                var black = waitingPlayers.Dequeue();
                                var game = new GameSession(white, black);
                                games.Add(game);
                                Task.Run(() => game.Start());
                            }
                        }
                    }
                    else
                    {
                        client.Close();
                    }
                }
            }
            catch
            {
                client.Close();
            }
        }
    }

    class GameSession
    {
        private TcpClient whiteClient;
        private TcpClient blackClient;
        private StreamReader whiteReader;
        private StreamWriter whiteWriter;
        private StreamReader blackReader;
        private StreamWriter blackWriter;
        private Board board;
        private PieceColor currentPlayer;

        public GameSession(TcpClient white, TcpClient black)
        {
            whiteClient = white;
            blackClient = black;

            whiteClient.ReceiveTimeout = 0;
            blackClient.ReceiveTimeout = 0;

            whiteReader = new StreamReader(whiteClient.GetStream(), Encoding.UTF8);
            whiteWriter = new StreamWriter(whiteClient.GetStream(), Encoding.UTF8) { AutoFlush = true };
            blackReader = new StreamReader(blackClient.GetStream(), Encoding.UTF8);
            blackWriter = new StreamWriter(blackClient.GetStream(), Encoding.UTF8) { AutoFlush = true };

            board = new Board();
            currentPlayer = PieceColor.White;
        }

        public void Start()
        {
            try
            {
                SendMessage(whiteWriter, "COLOR:White");
                SendMessage(blackWriter, "COLOR:Black");

                while (true)
                {
                    var boardState = GetBoardState();
                    SendMessage(whiteWriter, "BOARD:" + boardState);
                    SendMessage(blackWriter, "BOARD:" + boardState);

                    var activeWriter = currentPlayer == PieceColor.White ? whiteWriter : blackWriter;
                    var activeReader = currentPlayer == PieceColor.White ? whiteReader : blackReader;
                    var inactiveWriter = currentPlayer == PieceColor.White ? blackWriter : whiteWriter;

                    SendMessage(activeWriter, "YOURTURN");
                    SendMessage(inactiveWriter, "WAIT");

                    var moveStr = ReceiveMessage(activeReader);
                    if (string.IsNullOrEmpty(moveStr))
                    {
                        SendMessage(inactiveWriter, "WIN:Opponent disconnected");
                        break;
                    }

                    Console.WriteLine($"{currentPlayer} attempts move: {moveStr}");

                    var move = Parser.Parse(moveStr);
                    var piece = board.GetPiece(move.From);

                    if (piece == null || piece.Color != currentPlayer)
                    {
                        SendMessage(activeWriter, "ERROR:Not your piece");
                        continue;
                    }

                    if (!Rules.IsValidMove(move, board, piece))
                    {
                        SendMessage(activeWriter, "ERROR:Illegal move");
                        continue;
                    }

                    board.SetPiece(move.To, piece);
                    board.SetPiece(move.From, null!);

                    currentPlayer = currentPlayer == PieceColor.White ? PieceColor.Black : PieceColor.White;

                    if (board.IsWin(currentPlayer == PieceColor.White ? PieceColor.Black : PieceColor.White))
                    {
                        boardState = GetBoardState();
                        SendMessage(whiteWriter, "BOARD:" + boardState);
                        SendMessage(blackWriter, "BOARD:" + boardState);

                        var winner = currentPlayer == PieceColor.White ? "Black" : "White";
                        SendMessage(whiteWriter, "WIN:" + winner);
                        SendMessage(blackWriter, "WIN:" + winner);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Game error: {ex.Message}");
            }
            finally
            {
                whiteClient.Close();
                blackClient.Close();
            }
        }

        private string GetBoardState()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    var piece = board.GetPiece(new Position(i, j));
                    if (piece == null)
                    {
                        sb.Append("_");
                    }
                    else
                    {
                        sb.Append((char)piece.Color);
                        sb.Append((char)piece.Type);
                    }
                    sb.Append(",");
                }
            }
            return sb.ToString();
        }

        private void SendMessage(StreamWriter writer, string message)
        {
            writer.WriteLine(message);
        }

        private string ReceiveMessage(StreamReader reader)
        {
            return reader.ReadLine()?.Trim() ?? "";
        }
    }

    internal class Board
    {
        private readonly List<List<Piece?>> board = [[], [], [], [], [], [], [], []];

        public Board()
        {
            GenerateBoard();
        }

        public bool IsWin(PieceColor pieceColor)
        {
            PieceColor oppositeColor = pieceColor == PieceColor.White ? PieceColor.Black : PieceColor.White;
            Dictionary<Position, Piece> allOppositePieces = [];

            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    if (GetPiece(new(i, j))?.Color == oppositeColor)
                    {
                        allOppositePieces.Add(new(i, j), GetPiece(new(i, j))!);
                    }
                }
            }

            foreach (var oppositePiece in allOppositePieces)
            {
                for (var i = 0; i < 8; i++)
                {
                    for (var j = 0; j < 8; j++)
                    {
                        if (
                            Rules.IsValidMove(new(new(oppositePiece.Key.Row, oppositePiece.Key.Col), new(i, j)), this, oppositePiece.Value) &&
                            !IfCheckAfterMove(oppositePiece.Value, new(new(oppositePiece.Key.Row, oppositePiece.Key.Col), new(i, j)))
                        )
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        public bool IfCheckAfterMove(Piece piece, Move move)
        {
            Piece? tempPiece = GetPiece(move.To);

            SetPiece(move.To, piece);
            SetPiece(move.From, null!);

            bool isCheck = IsThereCheck(piece.Color);

            SetPiece(move.From, piece);
            SetPiece(move.To, tempPiece!);

            return isCheck;
        }

        public bool IsThereCheck(PieceColor pieceColor)
        {
            Position? kingPosition = null;
            Dictionary<Position, Piece> allEnemyPositions = [];
            PieceColor oppositeColor = pieceColor == PieceColor.White ? PieceColor.Black : PieceColor.White;

            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    if (GetPiece(new(i, j))?.Type == PieceType.King && GetPiece(new(i, j))?.Color == pieceColor)
                    {
                        kingPosition = new(i, j);
                    }
                }
            }

            for (var i = 0; i < 8; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    if (GetPiece(new(i, j))?.Color == oppositeColor)
                    {
                        allEnemyPositions.Add(new Position(i, j), GetPiece(new(i, j))!);
                    }
                }
            }

            foreach (var enemyPosition in allEnemyPositions)
            {
                if (kingPosition is not null)
                {
                    bool check = Rules.IsValidMove(new Move(enemyPosition.Key, (Position)kingPosition), this, enemyPosition.Value);

                    if (check)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public bool IsPathClear(Position from, Position to)
        {
            var dirRow = Math.Sign(to.Row - from.Row);
            var dirCol = Math.Sign(to.Col - from.Col);
            var current = new Position(from.Row + dirRow, from.Col + dirCol);
            while (current != to)
            {
                if (GetPiece(current) != null)
                {
                    return false;
                }
                current.Row += dirRow; current.Col += dirCol;
            }
            return true;
        }

        public Piece? GetPiece(Position position)
        {
            return board[position.Row][position.Col];
        }

        public void SetPiece(Position position, Piece piece)
        {
            board[position.Row][position.Col] = piece;
        }

        private void GenerateBoard()
        {
            List<Piece?> blackPieces = [
                new Piece(PieceColor.Black, PieceType.Rook),
                new Piece(PieceColor.Black, PieceType.Knight),
                new Piece(PieceColor.Black, PieceType.Bishop),
                new Piece(PieceColor.Black, PieceType.Queen),
                new Piece(PieceColor.Black, PieceType.King),
                new Piece(PieceColor.Black, PieceType.Bishop),
                new Piece(PieceColor.Black, PieceType.Knight),
                new Piece(PieceColor.Black, PieceType.Rook)
            ];

            List<Piece?> whitePieces = [
                new Piece(PieceColor.White, PieceType.Rook),
                new Piece(PieceColor.White, PieceType.Knight),
                new Piece(PieceColor.White, PieceType.Bishop),
                new Piece(PieceColor.White, PieceType.Queen),
                new Piece(PieceColor.White, PieceType.King),
                new Piece(PieceColor.White, PieceType.Bishop),
                new Piece(PieceColor.White, PieceType.Knight),
                new Piece(PieceColor.White, PieceType.Rook)
            ];

            board[0] = blackPieces;
            board[7] = whitePieces;

            for (var i = 0; i < 8; i++)
            {
                board[1].Add(new Piece(PieceColor.Black, PieceType.Pawn));
                board[6].Add(new Piece(PieceColor.White, PieceType.Pawn));
            }

            for (var i = 2; i < 6; i++)
            {
                for (var j = 0; j < 8; j++)
                {
                    board[i].Add(null);
                }
            }
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

    internal record struct Position(int Row, int Col)
    {
        public int Row = Row;
        public int Col = Col;
    }

    internal record struct Move(Position From, Position To)
    {
        public Position From = From;
        public Position To = To;
    }

    internal class Piece(PieceColor color, PieceType type)
    {
        public readonly PieceColor Color = color;
        public readonly PieceType Type = type;

        public readonly string Symbol = type switch
        {
            PieceType.King => color == PieceColor.Black ? " ♔ " : " ♚ ",
            PieceType.Queen => color == PieceColor.Black ? " ♕ " : " ♛ ",
            PieceType.Rook => color == PieceColor.Black ? " ♖ " : " ♜ ",
            PieceType.Pawn => color == PieceColor.Black ? " ♙ " : " ♟ ",
            PieceType.Knight => color == PieceColor.Black ? " ♘ " : " ♞ ",
            PieceType.Bishop => color == PieceColor.Black ? " ♗ " : " ♝ ",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    class Parser
    {
        public static Move Parse(string text)
        {
            var tokens = text.Split(" ");
            var from = ParseSquare(tokens[0]);
            var to = ParseSquare(tokens[1]);

            return new Move(from, to);
        }

        static Position ParseSquare(string squareText)
        {
            var col = squareText[0] - 'a';
            var row = 8 - int.Parse(squareText[1].ToString());

            return new Position(row, col);
        }
    }

    class Rules
    {
        public static bool IsValidMove(Move move, Board board, Piece currentPiece)
        {
            bool isPseudoLegal = false;

            if (currentPiece == null)
                return false;

            var target = board.GetPiece(move.To);

            if (target != null && target.Color == currentPiece.Color)
                return false;

            switch (currentPiece.Type)
            {
                case PieceType.Pawn:
                    isPseudoLegal = PawnMoves.IsLegal(move, board, currentPiece);
                    break;

                case PieceType.Knight:
                    isPseudoLegal = KnightMoves.IsLegal(move, board, currentPiece);
                    break;

                case PieceType.Bishop:
                    isPseudoLegal = BishopMoves.IsLegal(move, board, currentPiece);
                    break;

                case PieceType.Rook:
                    isPseudoLegal = RookMoves.IsLegal(move, board, currentPiece);
                    break;

                case PieceType.Queen:
                    isPseudoLegal = QueenMoves.IsLegal(move, board, currentPiece);
                    break;

                case PieceType.King:
                    isPseudoLegal = KingMoves.IsLegal(move, board, currentPiece);
                    break;
            }

            if (isPseudoLegal)
            {
                if (!board.IfCheckAfterMove(currentPiece, move))
                {
                    return true;
                }
            }

            return false;
        }
    }

    class KingMoves
    {
        public static bool IsLegal(Move move, Board board, Piece piece)
        {
            var dy = Math.Abs(move.To.Row - move.From.Row);
            var dx = Math.Abs(move.To.Col - move.From.Col);

            if (dx <= 1 && dy <= 1)
            {
                return true;
            }

            return false;
        }
    }

    class QueenMoves
    {
        public static bool IsLegal(Move move, Board board, Piece piece)
        {
            return RookMoves.IsLegal(move, board, piece) || BishopMoves.IsLegal(move, board, piece);
        }
    }

    class RookMoves
    {
        public static bool IsLegal(Move move, Board board, Piece piece)
        {
            var dy = Math.Abs(move.To.Row - move.From.Row);
            var dx = Math.Abs(move.To.Col - move.From.Col);

            if (dx != 0 && dy != 0)
            {
                return false;
            }

            return board.IsPathClear(move.From, move.To);
        }
    }

    class BishopMoves
    {
        public static bool IsLegal(Move move, Board board, Piece piece)
        {
            var dy = Math.Abs(move.To.Row - move.From.Row);
            var dx = Math.Abs(move.To.Col - move.From.Col);

            if (dx != dy)
            {
                return false;
            }

            return board.IsPathClear(move.From, move.To);
        }
    }

    class KnightMoves
    {
        public static bool IsLegal(Move move, Board board, Piece piece)
        {
            var dy = Math.Abs(move.To.Row - move.From.Row);
            var dx = Math.Abs(move.To.Col - move.From.Col);

            if (dy == 1 && dx == 2) return true;
            if (dx == 1 && dy == 2) return true;

            return false;
        }
    }

    class PawnMoves
    {
        public static bool IsLegal(Move move, Board board, Piece piece)
        {
            var direction = piece.Color == PieceColor.White ? -1 : 1;
            var dx = move.To.Col - move.From.Col;
            var dy = move.To.Row - move.From.Row;

            if (dx == 0)
            {
                if (dy == direction && board.GetPiece(move.To) == null)
                {
                    return true;
                }

                if (dy == 2 * direction && board.IsPathClear(move.From, move.To) && move.From.Row == (piece.Color == PieceColor.White ? 6 : 1) && board.GetPiece(move.To) is null)
                {
                    return true;
                }
            }

            if (Math.Abs(dx) == 1 && dy == direction && board.GetPiece(move.To)?.Color != piece.Color && board.GetPiece(move.To) is not null)
            {
                return true;
            }

            return false;
        }
    }
}
