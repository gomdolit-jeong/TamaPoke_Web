using System.Text.Json.Serialization;
using TamaPoke.Web.Sprites;

namespace TamaPoke.Web.Game;

public enum MiniGame { None, Ball, Catch, Memo, Clean }

public sealed record DirtItem(int Id, double X, double Y);

// 미니게임. 수치와 규칙은 PC판 PokemonState.MiniGames.cs와 같습니다.
public sealed partial class PokemonState
{
    private const double GAME_STEP_MS = 33;
    private const double GAME_RESULT_MS = 2000; // 끝난 뒤 결과를 보여주는 시간
    private const double CLEAN_HIT_MS = 500;
    private const int ARENA_CENTER = 144;
    private const double POINTER_LIMIT = 100;

    private static readonly string[] FruitImages =
    [
        "fruit_apple.png", "fruit_banana.png", "fruit_grapes.png", "fruit_strawberry.png",
        "fruit_orange.png", "fruit_watermelon.png", "fruit_cherry.png", "fruit_peach.png",
    ];

    private enum MemoPhase { Start, WatchWait, Lit, Gap, Input, InputFlash, Correct }

    // 저장되는 값
    public int TrSpeed { get; set; }
    public int GameHighScore { get; set; }
    public int CatchHighScore { get; set; }
    public int MemoHighScore { get; set; }
    public int CleanHighScore { get; set; }

    private double _gameStepElapsedMs;
    private double _gameResultMs;
    private double _targetX;
    private double _targetY;
    private double _gamePosY;

    [JsonIgnore] public bool IsPlayMenuOpen { get; set; }
    [JsonIgnore] public MiniGame ActiveGame { get; private set; }
    [JsonIgnore] public MiniGame ShownGame { get; private set; }
    [JsonIgnore] public bool IsAnyMiniGameOpen => ActiveGame != MiniGame.None;
    [JsonIgnore] public bool CanStartActivity => !IsEgg && !IsSleeping && !IsCeremony && !IsAnyMiniGameOpen;

    public void StartMiniGame(MiniGame game)
    {
        IsPlayMenuOpen = false;
        IsFeedMenuOpen = false;
        if (!CanStartActivity) return;

        ResetPosition();
        _targetX = 0;
        _targetY = 0;
        _gamePosY = 0;
        _gameStepElapsedMs = 0;
        _gameResultMs = 0;
        ActiveGame = game;
        ShownGame = game;

        switch (game)
        {
            case MiniGame.Ball:
                GameScore = 0;
                GameMisses = 0;
                RespawnBall();
                break;
            case MiniGame.Catch:
                CatchScore = 0;
                CatchTimeLeft = 10.0;
                SpawnFruit();
                break;
            case MiniGame.Memo:
                MemoScore = 0;
                _memoSequence.Clear();
                _memoSequence.Add(Random.Shared.Next(4));
                MemoLit = null;
                MemoStatusKey = "memo.start";
                SetMemoPhase(MemoPhase.Start, 2000);
                break;
            case MiniGame.Clean:
                CleanScore = 0;
                _dirtItems.Clear();
                _dirtIdCounter = 0;
                _dirtSpawnTimer = 0;
                _cleanHitMs = 0;
                CleanStatusKey = "clean.start";
                break;
        }
    }

    // 그만하기: 보상 없이 끝납니다.
    public void QuitMiniGame()
    {
        if (!IsAnyMiniGameOpen) return;
        ActiveGame = MiniGame.None;
        MemoLit = null;
        ResetPosition();
        _gamePosY = 0;
        _gameResultMs = GAME_RESULT_MS;
    }

    // 포인터 위치(게임판 좌표)를 포켓몬이 따라갈 위치로 바꿉니다.
    public void SetGamePointer(double x, double y)
    {
        if (!IsAnyMiniGameOpen) return;
        _targetX = Math.Clamp(x - ARENA_CENTER, -POINTER_LIMIT, POINTER_LIMIT);
        if (ActiveGame == MiniGame.Clean) _targetY = Math.Clamp(y - ARENA_CENTER, -POINTER_LIMIT, POINTER_LIMIT);
    }

    private void UpdateMiniGame(double ms)
    {
        _gameStepElapsedMs += ms;
        while (_gameStepElapsedMs >= GAME_STEP_MS && IsAnyMiniGameOpen)
        {
            _gameStepElapsedMs -= GAME_STEP_MS;
            switch (ActiveGame)
            {
                case MiniGame.Ball: StepBallGame(); break;
                case MiniGame.Catch: StepCatchGame(); break;
                case MiniGame.Memo: StepMemoGame(); break;
                case MiniGame.Clean: StepCleanGame(); break;
            }
        }
    }

    private void UpdateGameResult(double ms)
    {
        if (ShownGame == MiniGame.None || IsAnyMiniGameOpen) return;
        _gameResultMs -= ms;
        if (_gameResultMs <= 0) ShownGame = MiniGame.None;
    }

    private void EndMiniGame(int poseLoopTicks = 90)
    {
        ActiveGame = MiniGame.None;
        ResetPosition();
        _gamePosY = 0;
        PlayTempAction(PokemonAnim.POSE, poseLoopTicks * LOOP_TICK_MS);
        _gameResultMs = GAME_RESULT_MS;
        NeedsSave = true;
    }

    private void Chase(double rate, double flipThreshold)
    {
        double chase = Math.Clamp((_targetX - PosX) * rate, -12, 12);
        PosX += chase;
        if (chase > flipThreshold) FlipX = 1;
        else if (chase < -flipThreshold) FlipX = -1;
    }

    // --- 1. 공 튕기기 ---
    private double _ballVelX;
    private double _ballVelY;

    [JsonIgnore] public int GameScore { get; private set; }
    [JsonIgnore] public int GameMisses { get; private set; }
    [JsonIgnore] public double BallX { get; private set; }
    [JsonIgnore] public double BallY { get; private set; }

    private void RespawnBall()
    {
        BallX = 50 + Random.Shared.Next(80);
        BallY = 40;
        double speed = Math.Min(1.2 + GameScore * 0.04, 3.0);
        _ballVelX = Random.Shared.Next(2) == 0 ? speed : -speed;
        _ballVelY = 0;
    }

    private void StepBallGame()
    {
        _ballVelY += Math.Min(0.35 + GameScore * 0.01, 0.7);
        BallX += _ballVelX;
        BallY += _ballVelY;

        double dx = BallX - ARENA_CENTER;
        double dy = BallY - ARENA_CENTER;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist > 130)
        {
            double nx = dx / dist;
            double ny = dy / dist;
            double dot = _ballVelX * nx + _ballVelY * ny;
            if (dot > 0)
            {
                _ballVelX = (_ballVelX - 2 * dot * nx) * 0.85;
                _ballVelY = (_ballVelY - 2 * dot * ny) * 0.85;
            }
            BallX = ARENA_CENTER + nx * 130;
            BallY = ARENA_CENTER + ny * 130;
        }

        if (BallY > 260)
        {
            GameMisses++;
            if (GameMisses >= 3)
            {
                EndBallGame();
                return;
            }
            RespawnBall();
        }

        Chase(0.35, 1.0);
    }

    public void TapBall()
    {
        if (ActiveGame != MiniGame.Ball) return;

        double dx = BallX + 16 - (ARENA_CENTER + PosX);
        double dy = BallY + 16 - 160;
        if (Math.Sqrt(dx * dx + dy * dy) > 70) return;

        GameScore++;
        _ballVelY = -(5.0 + (GameScore > 16 ? 2.5 : GameScore * 0.15));
        _ballVelX = Math.Clamp(_ballVelX + dx * 0.05, -5.0, 5.0);
    }

    private void EndBallGame()
    {
        if (GameScore > GameHighScore) GameHighScore = GameScore;
        TrSpeed = Math.Min(100, TrSpeed + GameScore / 5);
        Joy = Clamp100(Joy + 5 + (GameScore > 15 ? 30 : GameScore * 2));
        Energy = Math.Max(Energy - (10 + GameScore / 2), 5);
        Fullness = Math.Max(Fullness - 5, 5);
        Weight = Math.Max(Weight - GameScore * 2, 0);
        Bond = Clamp100(Bond + 2);
        EndMiniGame();
    }

    // --- 2. 과일 받기 ---
    [JsonIgnore] public int CatchScore { get; private set; }
    [JsonIgnore] public double CatchTimeLeft { get; private set; }
    [JsonIgnore] public double FruitX { get; private set; }
    [JsonIgnore] public double FruitY { get; private set; }
    [JsonIgnore] public string FruitImage { get; private set; } = FruitImages[0];

    private void SpawnFruit()
    {
        FruitY = -30;
        FruitX = Random.Shared.Next(30, 250);
        FruitImage = FruitImages[Random.Shared.Next(FruitImages.Length)];
    }

    private void StepCatchGame()
    {
        CatchTimeLeft -= GAME_STEP_MS / 1000;
        if (CatchTimeLeft <= 0)
        {
            CatchTimeLeft = 0;
            EndCatchGame();
            return;
        }

        Chase(0.25, 0.5);
        FruitY += 3.5 + CatchScore * 0.15;

        double pokeX = ARENA_CENTER + PosX;
        const double pokeY = 170;
        if (FruitY > pokeY - 30 && FruitY < pokeY + 30 && Math.Abs(pokeX - FruitX) < 45)
        {
            CatchScore++;
            SpawnFruit();
        }
        if (FruitY > 288) SpawnFruit();
    }

    private void EndCatchGame()
    {
        if (CatchScore > CatchHighScore) CatchHighScore = CatchScore;
        TrSpeed = Math.Min(100, TrSpeed + CatchScore / 5);
        Fullness = Clamp100(Fullness + 10 + CatchScore);
        Energy = Math.Max(Energy - 10, 5);
        EndMiniGame();
    }

    // --- 3. 리듬 메모리 ---
    private readonly List<int> _memoSequence = [];
    private MemoPhase _memoPhase;
    private double _memoTimerMs;
    private int _memoShowIndex;
    private int _memoInputIndex;
    private int _memoPendingInput;

    [JsonIgnore] public int MemoScore { get; private set; }
    [JsonIgnore] public int? MemoLit { get; private set; }
    [JsonIgnore] public string MemoStatusKey { get; private set; } = "memo.start";

    private void SetMemoPhase(MemoPhase phase, double ms)
    {
        _memoPhase = phase;
        _memoTimerMs = ms;
    }

    private void StepMemoGame()
    {
        if (_memoPhase == MemoPhase.Input) return;

        _memoTimerMs -= GAME_STEP_MS;
        if (_memoTimerMs > 0) return;

        switch (_memoPhase)
        {
            case MemoPhase.Start:
            case MemoPhase.Correct:
                MemoStatusKey = "memo.watch";
                _memoShowIndex = 0;
                SetMemoPhase(MemoPhase.WatchWait, 500);
                break;
            case MemoPhase.WatchWait:
                ShowNextMemo();
                break;
            case MemoPhase.Lit:
                MemoLit = null;
                SetMemoPhase(MemoPhase.Gap, 200);
                break;
            case MemoPhase.Gap:
                ShowNextMemo();
                break;
            case MemoPhase.InputFlash:
                MemoLit = null;
                ResolveMemoInput();
                break;
        }
    }

    private void ShowNextMemo()
    {
        if (_memoShowIndex < _memoSequence.Count)
        {
            MemoLit = _memoSequence[_memoShowIndex++];
            SetMemoPhase(MemoPhase.Lit, 400);
            return;
        }

        _memoInputIndex = 0;
        MemoStatusKey = "memo.yourTurn";
        _memoPhase = MemoPhase.Input;
    }

    public void SubmitMemoInput(int button)
    {
        if (ActiveGame != MiniGame.Memo || _memoPhase != MemoPhase.Input) return;

        _memoPendingInput = button;
        MemoLit = button;
        SetMemoPhase(MemoPhase.InputFlash, 200);
    }

    private void ResolveMemoInput()
    {
        if (_memoSequence[_memoInputIndex] != _memoPendingInput)
        {
            EndMemoGame();
            return;
        }

        _memoInputIndex++;
        if (_memoInputIndex < _memoSequence.Count)
        {
            _memoPhase = MemoPhase.Input;
            return;
        }

        MemoScore++;
        _memoSequence.Add(Random.Shared.Next(4));
        MemoStatusKey = "memo.correct";
        SetMemoPhase(MemoPhase.Correct, 1000);
    }

    private void EndMemoGame()
    {
        MemoStatusKey = "memo.wrong";
        if (MemoScore > MemoHighScore) MemoHighScore = MemoScore;
        Bond = Clamp100(Bond + 5 + MemoScore * 2);
        Energy = Math.Max(Energy - 5, 5);
        Fullness = Math.Max(Fullness - 2, 5);
        EndMiniGame();
    }

    // --- 4. 오염 청소 ---
    private readonly List<DirtItem> _dirtItems = [];
    private double _dirtSpawnTimer;
    private int _dirtIdCounter;
    private double _cleanHitMs;

    [JsonIgnore] public int CleanScore { get; private set; }
    [JsonIgnore] public IReadOnlyList<DirtItem> DirtItems => _dirtItems;
    [JsonIgnore] public string CleanStatusKey { get; private set; } = "clean.start";
    [JsonIgnore] public bool IsCleanHit => _cleanHitMs > 0;

    private void StepCleanGame()
    {
        if (_cleanHitMs > 0) _cleanHitMs -= GAME_STEP_MS;

        Chase(0.25, 0.5);
        _gamePosY += Math.Clamp((_targetY - _gamePosY) * 0.25, -12, 12);

        _dirtSpawnTimer += GAME_STEP_MS / 1000;
        if (_dirtSpawnTimer < Math.Max(0.3, 1.5 - CleanScore * 0.05)) return;

        _dirtSpawnTimer = 0;
        SpawnDirt();
    }

    private void SpawnDirt()
    {
        double x, y;
        do
        {
            double angle = Random.Shared.NextDouble() * Math.PI * 2;
            double radius = Random.Shared.NextDouble() * 110;
            x = ARENA_CENTER + Math.Cos(angle) * radius - 15;
            y = ARENA_CENTER + Math.Sin(angle) * radius - 15;
        }
        while (x is > 90 and < 190 && y > 230); // 그만하기 버튼과 겹치지 않게

        _dirtItems.Add(new DirtItem(++_dirtIdCounter, x, y));
        if (_dirtItems.Count >= 3) EndCleanGame();
    }

    public void TapDirt(int id)
    {
        if (ActiveGame != MiniGame.Clean) return;

        int index = _dirtItems.FindIndex(d => d.Id == id);
        if (index < 0) return;

        _dirtItems.RemoveAt(index);
        CleanScore++;
        _cleanHitMs = CLEAN_HIT_MS;
    }

    private void EndCleanGame()
    {
        CleanStatusKey = _dirtItems.Count >= 3 ? "clean.tooMany" : "clean.done";
        if (CleanScore > CleanHighScore) CleanHighScore = CleanScore;
        Hygiene = Clamp100(Hygiene + 10 + CleanScore);
        Bond = Clamp100(Bond + 2 + CleanScore / 5);
        Energy = Math.Max(Energy - 5, 5);
        EndMiniGame(30);
    }
}
