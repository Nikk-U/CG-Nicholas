using System;
using System.Collections.Generic;
using Unity.Netcode; // Import Netcode namespace
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Manages the visual representation of the chess board and piece placement.
/// Inherits from MonoBehaviourSingleton to ensure only one instance exists.
/// </summary>
public class BoardManager : MonoBehaviourSingleton<BoardManager>
{
    // Array holding references to all square GameObjects (64 squares for an 8x8 board).
    private readonly GameObject[] allSquaresGO = new GameObject[64];
    
    // Dictionary mapping board squares to their corresponding GameObjects.
    private Dictionary<Square, GameObject> positionMap;

    // Constant representing the side length of the board plane (from centre to centre of corner squares).
    private const float BoardPlaneSideLength = 14f; // measured from corner square centre to corner square centre, on same side.
    
    // Half the side length, for convenience.
    private const float BoardPlaneSideHalfLength = BoardPlaneSideLength * 0.5f;
    
    // The vertical offset for placing the board (height above the base).
    private const float BoardHeight = 1.6f;

    /// <summary>
    /// Public Property to access positionMap.
    /// </summary>
    public Dictionary<Square, GameObject> PositionMap
    {
        get { return positionMap; }
    }

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// Sets up the board, subscribes to game events, and creates the square GameObjects.
    /// </summary>
    private void Awake()
    {
        // Subscribing to events
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        GameManager.GameResetToHalfMoveEvent += OnGameResetToHalfMove;

        // Initializing positionMap
        positionMap = new Dictionary<Square, GameObject>(64);

        Transform boardTransform = transform;
        Vector3 boardPosition = boardTransform.position;

        for (int file = 1; file <= 8; file++)
        {
            for (int rank = 1; rank <= 8; rank++)
            {
                GameObject squareGO = new GameObject(SquareToString(file, rank))
                {
                    transform =
                    {
                        position = new Vector3(
                            boardPosition.x + FileOrRankToSidePosition(file),
                            boardPosition.y + BoardHeight,
                            boardPosition.z + FileOrRankToSidePosition(rank)
                        ),
                        parent = boardTransform
                    },
                    tag = "Square"
                };

                // Adding the square GameObject to positionMap
                positionMap.Add(new Square(file, rank), squareGO);
                allSquaresGO[(file - 1) * 8 + (rank - 1)] = squareGO;
            }
        }
    }

    /// <summary>
    /// Called when a new game is started.
    /// Clears the board and places pieces in their initial positions.
    /// </summary>
    private void OnNewGameStarted()
    {
        ClearBoard();
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            CreateAndPlacePieceGO(piece, square);
        }
        EnsureOnlyPiecesOfSideAreEnabled(GameManager.Instance.SideToMove);
    }

    /// <summary>
    /// Called when the game is reset to a particular half-move.
    /// Clears the board and updates the game pieces.
    /// </summary>
    private void OnGameResetToHalfMove()
    {
        ClearBoard();
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            CreateAndPlacePieceGO(piece, square);
        }

        GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
        {
            SetActiveAllPieces(false);
        }
        else
        {
            EnsureOnlyPiecesOfSideAreEnabled(GameManager.Instance.SideToMove);
        }
    }

    public void CreateAndPlacePieceGO(Piece piece, Square position)
    {
        string modelName = $"{piece.Owner} {piece.GetType().Name}";
        GameObject prefab = Resources.Load<GameObject>("PieceSets/Marble/" + modelName);
        GameObject pieceGO = Instantiate(prefab, positionMap[position].transform);

        // Only spawn the object on the server to avoid duplication
        if (pieceGO.GetComponent<NetworkObject>() != null && NetworkManager.Singleton.IsServer && !pieceGO.GetComponent<NetworkObject>().IsSpawned)
        {
            pieceGO.GetComponent<NetworkObject>().Spawn(true);
            Debug.Log($"Spawning {modelName} at {position}.");
        }
        else
        {
            Debug.Log($"Skipping spawn of {modelName} as it is already spawned.");
        }
    }

    // Clears all visual pieces from the board
    private void ClearBoard()
    {
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            if (pieceBehaviour.GetComponent<NetworkObject>() != null && NetworkManager.Singleton.IsServer)
            {
                pieceBehaviour.GetComponent<NetworkObject>().Despawn(true);
            }
            DestroyImmediate(pieceBehaviour.gameObject);
        }
    }

    // Ensures only pieces of the current side are enabled (i.e., not pieces of the opponent)
    public void EnsureOnlyPiecesOfSideAreEnabled(Side side)
    {
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            Piece piece = GameManager.Instance.CurrentBoard[pieceBehaviour.CurrentSquare];
            pieceBehaviour.enabled = pieceBehaviour.PieceColor == side && GameManager.Instance.HasLegalMoves(piece);
        }
    }

    // Method to handle the activation of all pieces
    public void SetActiveAllPieces(bool active)
    {
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            pieceBehaviour.enabled = active;
        }
    }

    // Get the square GameObject by position
    public GameObject GetSquareGOByPosition(Square position) =>
        Array.Find(allSquaresGO, go => go.name == SquareToString(position));

    // Helper method to calculate positions for squares
    private static float FileOrRankToSidePosition(int index)
    {
        float t = (index - 1) / 7f;
        return Mathf.Lerp(-BoardPlaneSideHalfLength, BoardPlaneSideHalfLength, t);
    }

    // Other utility methods (e.g., for checking square positions, etc.)
}
using System;
using System.Collections.Generic;
using Unity.Netcode; // Import Netcode namespace
using UnityChess;
using UnityEngine;
using static UnityChess.SquareUtil;

/// <summary>
/// Manages the visual representation of the chess board and piece placement.
/// Inherits from MonoBehaviourSingleton to ensure only one instance exists.
/// </summary>
public class BoardManager : MonoBehaviourSingleton<BoardManager>
{
    // Array holding references to all square GameObjects (64 squares for an 8x8 board).
    private readonly GameObject[] allSquaresGO = new GameObject[64];

    // Dictionary mapping board squares to their corresponding GameObjects.
    private Dictionary<Square, GameObject> positionMap;

    // Constant representing the side length of the board plane (from centre to centre of corner squares).
    private const float BoardPlaneSideLength = 14f; // measured from corner square centre to corner square centre, on same side.

    // Half the side length, for convenience.
    private const float BoardPlaneSideHalfLength = BoardPlaneSideLength * 0.5f;

    // The vertical offset for placing the board (height above the base).
    private const float BoardHeight = 1.6f;

    /// <summary>
    /// Public Property to access positionMap.
    /// </summary>
    public Dictionary<Square, GameObject> PositionMap
    {
        get { return positionMap; }
    }

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// Sets up the board, subscribes to game events, and creates the square GameObjects.
    /// </summary>
    private void Awake()
    {
        // Subscribing to events
        GameManager.NewGameStartedEvent += OnNewGameStarted;
        GameManager.GameResetToHalfMoveEvent += OnGameResetToHalfMove;

        // Initializing positionMap
        positionMap = new Dictionary<Square, GameObject>(64);

        Transform boardTransform = transform;
        Vector3 boardPosition = boardTransform.position;

        for (int file = 1; file <= 8; file++)
        {
            for (int rank = 1; rank <= 8; rank++)
            {
                GameObject squareGO = new GameObject(SquareToString(file, rank))
                {
                    transform =
                    {
                        position = new Vector3(
                            boardPosition.x + FileOrRankToSidePosition(file),
                            boardPosition.y + BoardHeight,
                            boardPosition.z + FileOrRankToSidePosition(rank)
                        ),
                        parent = boardTransform
                    },
                    tag = "Square"
                };

                // Adding the square GameObject to positionMap
                positionMap.Add(new Square(file, rank), squareGO);
                allSquaresGO[(file - 1) * 8 + (rank - 1)] = squareGO;
            }
        }
    }

    /// <summary>
    /// Called when a new game is started.
    /// Clears the board and places pieces in their initial positions.
    /// </summary>
    private void OnNewGameStarted()
    {
        ClearBoard();
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            CreateAndPlacePieceGO(piece, square);
        }
        EnsureOnlyPiecesOfSideAreEnabled(GameManager.Instance.SideToMove);
    }

    /// <summary>
    /// Called when the game is reset to a particular half-move.
    /// Clears the board and updates the game pieces.
    /// </summary>
    private void OnGameResetToHalfMove()
    {
        ClearBoard();
        foreach ((Square square, Piece piece) in GameManager.Instance.CurrentPieces)
        {
            CreateAndPlacePieceGO(piece, square);
        }

        GameManager.Instance.HalfMoveTimeline.TryGetCurrent(out HalfMove latestHalfMove);
        if (latestHalfMove.CausedCheckmate || latestHalfMove.CausedStalemate)
        {
            SetActiveAllPieces(false);
        }
        else
        {
            EnsureOnlyPiecesOfSideAreEnabled(GameManager.Instance.SideToMove);
        }
    }

    public void CreateAndPlacePieceGO(Piece piece, Square position)
    {
        string modelName = $"{piece.Owner} {piece.GetType().Name}";
        GameObject prefab = Resources.Load<GameObject>("PieceSets/Marble/" + modelName);
        GameObject pieceGO = Instantiate(prefab, positionMap[position].transform);

        // Only spawn the object on the server to avoid duplication
        if (pieceGO.GetComponent<NetworkObject>() != null && NetworkManager.Singleton.IsServer && !pieceGO.GetComponent<NetworkObject>().IsSpawned)
        {
            pieceGO.GetComponent<NetworkObject>().Spawn(true);
            Debug.Log($"Spawning {modelName} at {position}.");
        }
        else
        {
            Debug.Log($"Skipping spawn of {modelName} as it is already spawned.");
        }
    }

    // Clears all visual pieces from the board
    private void ClearBoard()
    {
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            if (pieceBehaviour.GetComponent<NetworkObject>() != null && NetworkManager.Singleton.IsServer)
            {
                pieceBehaviour.GetComponent<NetworkObject>().Despawn(true);
            }
            DestroyImmediate(pieceBehaviour.gameObject);
        }
    }

    // Ensures only pieces of the current side are enabled (i.e., not pieces of the opponent)
    public void EnsureOnlyPiecesOfSideAreEnabled(Side side)
    {
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            Piece piece = GameManager.Instance.CurrentBoard[pieceBehaviour.CurrentSquare];
            pieceBehaviour.enabled = pieceBehaviour.PieceColor == side && GameManager.Instance.HasLegalMoves(piece);
        }
    }

    // Method to handle the activation of all pieces
    public void SetActiveAllPieces(bool active)
    {
        VisualPiece[] visualPiece = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece pieceBehaviour in visualPiece)
        {
            pieceBehaviour.enabled = active;
        }
    }

    // Get the square GameObject by position
    public GameObject GetSquareGOByPosition(Square position) =>
        Array.Find(allSquaresGO, go => go.name == SquareToString(position));

    // Helper method to calculate positions for squares
    private static float FileOrRankToSidePosition(int index)
    {
        float t = (index - 1) / 7f;
        return Mathf.Lerp(-BoardPlaneSideHalfLength, BoardPlaneSideHalfLength, t);
    }

    // Method to handle castling rook movement
    public void CastleRook(Square rookSquare, Square rookEndSquare)
    {
        GameObject rookGO = GetPieceGOAtPosition(rookSquare);
        if (rookGO != null)
        {
            rookGO.transform.position = GetSquareGOByPosition(rookEndSquare).transform.position;
            rookGO.transform.parent = GetSquareGOByPosition(rookEndSquare).transform;
        }
    }

    // Method to destroy a visual piece at a given position
    public void TryDestroyVisualPiece(Square position)
    {
        GameObject pieceGO = GetPieceGOAtPosition(position);
        if (pieceGO != null)
        {
            if (pieceGO.GetComponent<NetworkObject>() != null && NetworkManager.Singleton.IsServer)
            {
                pieceGO.GetComponent<NetworkObject>().Despawn(true);
            }
            Destroy(pieceGO);
        }
    }

    // Method to get the piece GameObject at a given position
    public GameObject GetPieceGOAtPosition(Square position)
    {
        VisualPiece[] visualPieces = GetComponentsInChildren<VisualPiece>(true);
        foreach (VisualPiece piece in visualPieces)
        {
            if (piece.CurrentSquare.Equals(position))
            {
                return piece.gameObject;
            }
        }
        return null;
    }
}private async void OnPieceMoved(Square movedPieceInitialSquare, Transform movedPieceTransform, Transform closestBoardSquareTransform, Piece promotionPiece = null)
{
    Square endSquare = new Square(closestBoardSquareTransform.name);

    if (!game.TryGetLegalMove(movedPieceInitialSquare, endSquare, out Movement move))
    {
        movedPieceTransform.position = movedPieceTransform.parent.position;
#if DEBUG_VIEW
        Piece movedPiece = CurrentBoard[movedPieceInitialSquare];
        game.TryGetLegalMovesForPiece(movedPiece, out ICollection<Movement> legalMoves);
        UnityChessDebug.ShowLegalMovesInLog(legalMoves);
#endif
        return;
    }

    if (move is PromotionMove promotionMove)
    {
        promotionMove.SetPromotionPiece(promotionPiece);
    }

    if ((move is not SpecialMove specialMove || await TryHandleSpecialMoveBehaviourAsync(specialMove))
        && TryExecuteMove(move))
    {
        if (move is not SpecialMove)
        {
            BoardManager.Instance.TryDestroyVisualPiece(move.End);
        }

        if (move is PromotionMove)
        {
            movedPieceTransform = BoardManager.Instance.GetPieceGOAtPosition(move.End).transform;
        }

        movedPieceTransform.parent = closestBoardSquareTransform;
        movedPieceTransform.position = closestBoardSquareTransform.position;

        if (movedPieceTransform.GetComponent<NetworkObject>() != null && NetworkManager.Singleton.IsServer)
        {
            movedPieceTransform.GetComponent<NetworkObject>().Despawn(true);
            movedPieceTransform.GetComponent<NetworkObject>().Spawn(true);
        }
    }
}

private async Task<bool> TryHandleSpecialMoveBehaviourAsync(SpecialMove specialMove)
{
    switch (specialMove)
    {
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
                || promotionUITaskCancellationTokenSource.Token.IsCancellationRequested)
            {
                return false;
            }

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

private ElectedPiece GetUserPromotionPieceChoice()
{
    while (userPromotionChoice == ElectedPiece.None) { }

    ElectedPiece result = userPromotionChoice;
    userPromotionChoice = ElectedPiece.None;
    return result;
}

public void ElectPiece(ElectedPiece choice)
{
    userPromotionChoice = choice;
}