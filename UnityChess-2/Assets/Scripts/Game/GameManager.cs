using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityChess;
using UnityEngine;

public class GameManager : MonoBehaviourSingleton<GameManager> {
    public static event Action NewGameStartedEvent;
    public static event Action GameEndedEvent;
    public static event Action GameResetToHalfMoveEvent;
    public static event Action MoveExecutedEvent;

    public Board CurrentBoard {
        get {
            game.BoardTimeline.TryGetCurrent(out Board currentBoard);
            return currentBoard;
        }
    }

    public Side SideToMove {
        get {
            game.ConditionsTimeline.TryGetCurrent(out GameConditions currentConditions);
            return currentConditions.SideToMove;
        }
    }

    public Side StartingSide => game.ConditionsTimeline[0].SideToMove;

    public Timeline<HalfMove> HalfMoveTimeline => game.HalfMoveTimeline;

    public int LatestHalfMoveIndex => game.HalfMoveTimeline.HeadIndex;

    public int FullMoveNumber => StartingSide switch {
        Side.White => LatestHalfMoveIndex / 2 + 1,
        Side.Black => (LatestHalfMoveIndex + 1) / 2 + 1,
        _ => -1
    };

    private bool isWhiteAI;
    private bool isBlackAI;

    public List<(Square, Piece)> CurrentPieces {
        get {
            currentPiecesBacking.Clear();
            for (int file = 1; file <= 8; file++) {
                for (int rank = 1; rank <= 8; rank++) {
                    Piece piece = CurrentBoard[file, rank];
                    if (piece != null) currentPiecesBacking.Add((new Square(file, rank), piece));
                }
            }
            return currentPiecesBacking;
        }
    }

    private readonly List<(Square, Piece)> currentPiecesBacking = new List<(Square, Piece)>();

    [SerializeField] private UnityChessDebug unityChessDebug;
    private Game game;
    private FENSerializer fenSerializer;
    private PGNSerializer pgnSerializer;
    private CancellationTokenSource promotionUITaskCancellationTokenSource;
    private ElectedPiece userPromotionChoice = ElectedPiece.None;
    private Dictionary<GameSerializationType, IGameSerializer> serializersByType;
    private GameSerializationType selectedSerializationType = GameSerializationType.FEN;

    public void Start() {
        VisualPiece.VisualPieceMoved += OnPieceMoved;

        serializersByType = new Dictionary<GameSerializationType, IGameSerializer> {
            [GameSerializationType.FEN] = new FENSerializer(),
            [GameSerializationType.PGN] = new PGNSerializer()
        };

        StartNewGame();

#if DEBUG_VIEW
        unityChessDebug.gameObject.SetActive(true);
        unityChessDebug.enabled = true;
#endif
    }

    public async void StartNewGame() {
        game = new Game();
        NewGameStartedEvent?.Invoke();
    }

    public string SerializeGame() {
        return serializersByType.TryGetValue(selectedSerializationType, out IGameSerializer serializer)
            ? serializer?.Serialize(game)
            : null;
    }

    public void LoadGame(string serializedGame) {
        game = serializersByType[selectedSerializationType].Deserialize(serializedGame);
        NewGameStartedEvent?.Invoke();
    }

    public void ResetGameToHalfMoveIndex(int halfMoveIndex) {
        if (!game.ResetGameToHalfMoveIndex(halfMoveIndex)) return;

        UIManager.Instance.SetActivePromotionUI(false);
        promotionUITaskCancellationTokenSource?.Cancel();
        GameResetToHalfMoveEvent?.Invoke();
    }

    private bool TryExecuteMove(Movement move) {
        if (!game.TryExecuteMove(move)) {
            return false;
        }

        HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);

        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate) {
            BoardManager.Instance.SetActiveAllPieces(false);
            GameEndedEvent?.Invoke();
        } else {
            BoardManager.Instance.EnsureOnlyPiecesOfSideAreEnabled(SideToMove);
        }

        MoveExecutedEvent?.Invoke();

        return true;
    }

    private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove) {
        switch (specialMove) {
            case CastlingMove castlingMove:
                BoardManager.Instance.CastleRook(castlingMove.RookSquare, castlingMove.GetRookEndSquare());
                return true;
            case EnPassantMove enPassantMove:
                BoardManager.Instance.TryDestroyVisualPiece(enPassantMove.CapturedPawnSquare);
                return true;
            case PromotionMove { PromotionPiece: null } promotionMove:
                UIManager.Instance.SetActivePromotionUI(true);
                BoardManager.Instance.SetActiveAllPieces(false);

                promotionUITaskCancellationTokenSource?.Cancel();
                promotionUITaskCancellationTokenSource = new CancellationTokenSource();

                ElectedPiece choice = await Task.Run(GetUserPromotionPieceChoice, promotionUITaskCancellationTokenSource.Token);

                UIManager.Instance.SetActivePromotionUI(false);
                BoardManager.Instance.SetActiveAllPieces(true);

                if (promotionUITaskCancellationTokenSource == null
                    || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested
                ) { return false; }

                promotionMove.SetPromotionPiece(
                    PromotionUtil.GeneratePromotionPiece(choice, SideToMove)
                );
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                promotionUITaskCancellationTokenSource = null;
                return true;
            case PromotionMove promotionMove:
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.Start);
                BoardManager.Instance.TryDestroyVisualPiece(promotionMove.End);
                BoardManager.Instance.CreateAndPlacePieceGO(promotionMove.PromotionPiece, promotionMove.End);

                return true;
            default:
                return false;
        }
    }

    private ElectedPiece GetUserPromotionPieceChoice() {
        while (userPromotionChoice == ElectedPiece.None) { }

        ElectedPiece result = userPromotionChoice;
        userPromotionChoice = ElectedPiece.None;
        return result;
    }

    public void ElectPiece(ElectedPiece choice) {
        userPromotionChoice = choice;
    }

    private async void OnPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null) {
        if (closestBoardSquareTransform == null || string.IsNullOrEmpty(closestBoardSquareTransform.name)) {
            Debug.LogError("Invalid closest board square transform.");
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        Square endSquare;
        try {
            endSquare = SquareUtil.StringToSquare(closestBoardSquareTransform.name);
        } catch (Exception ex) {
            Debug.LogError($"Error parsing square: {ex.Message}");
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        if (!IsValidSquare(movedPieceInitialSquare) || !IsValidSquare(endSquare)) {
            Debug.LogError("Invalid square position.");
            movedPieceTransform.position = movedPieceTransform.parent.position;
            return;
        }

        if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move)) {
            movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
            Piece movedPiece = CurrentBoard[movedPieceInitialSquare];
            game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
            UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
            return;
        }

        if (move is PromotionMove promotionMove) {
            promotionMove.SetPromotionPiece(promotionPiece);
        }

        if ((move is not SpecialMove specialMove || await TryHandleSpecialMoveBehaviourAsync(specialMove))
            && TryExecuteMove(move)
        ) {
            if (move is not SpecialMove) { BoardManager.Instance.TryDestroyVisualPiece(move.End); }

            if (move is PromotionMove) {
                movedPieceTransform = BoardManager.Instance.GetPieceGOAtPosition(move.End).transform;
            }

            movedPieceTransform.parent = closestBoardSquareTransform;
            movedPieceTransform.position = closestBoardSquareTransform.position;
        }
    }

    public bool HasLegalMoves(Piece piece) {
        return game.TryGetLegalMovesForPiece(piece, out _);
    }

    private bool IsValidSquare(Square square) {
        return square.File >= 1 && square.File <= 8 && square.Rank >= 1 && square.Rank <= 8;
    }
}