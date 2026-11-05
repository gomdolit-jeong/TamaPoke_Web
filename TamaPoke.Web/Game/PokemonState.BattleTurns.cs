using TamaPoke.Web.Sprites;
using TamaPoke.Web.Utils;

namespace TamaPoke.Web.Game;

public enum BattleAction { Dodge, Run }

// 턴 진행, 기술 효과, 기술 배우기. PC판 PokemonState.Battle.cs / BattleStats.cs / Skills.cs에서 그대로 옮겼습니다.
public sealed partial class PokemonState
{
    private void GenerateEnemySkills()
    {
        for (int i = 0; i < 4; i++) EnemySkills[i] = 0;
        var d = Dex.Get(EnemySpeciesId);
        if (d == null) return;

        int n = SkillBook.GetLearnCount(EnemySpeciesId);
        if (n == 0)
        {
            EnemySkills[0] = 1;
            int fallback = SkillBook.GetFallbackMove(d.Type1);
            if (fallback != 1) EnemySkills[1] = fallback;
            return;
        }

        int[] score = new int[4];
        for (int pass = 0; pass < 2; pass++)
        {
            bool tmPass = (pass == 1);
            for (int i = 0; i < n; i++)
            {
                int at = SkillBook.GetLearnLevel(EnemySpeciesId, i);
                if (tmPass != (at == 0)) continue;
                if (!tmPass && at > EnemyLevel) continue;
                if (tmPass && EnemyLevel < 30) continue;

                int mv = SkillBook.GetLearnMove(EnemySpeciesId, i);
                if (mv == 0 || EnemySkills.Contains(mv)) continue;

                var m = SkillBook.GetSkill(mv);
                if (m == null || !m.IsSupported) continue; // 일격필살/카운터/회복기 등 효과가 없는 기술 제외

                int sc = (m.Category == SkillCategory.Status) ? 10 : m.Power + 20;
                if (m.Category != SkillCategory.Status && (m.Type == d.Type1 || m.Type == d.Type2)) sc += 40;

                sc += (at == 0 ? 50 : at);

                int slot = -1;
                for (int s = 0; s < 4; s++) { if (sc > score[s]) { slot = s; break; } }
                if (slot < 0) continue;
                for (int s = 3; s > slot; s--) { score[s] = score[s - 1]; EnemySkills[s] = EnemySkills[s - 1]; }
                score[slot] = sc; EnemySkills[slot] = mv;
            }
        }

        bool hasAttack = false;
        for (int i = 0; i < 4; i++)
        {
            if (EnemySkills[i] != 0 && SkillBook.GetSkill(EnemySkills[i])?.Power > 0)
            {
                hasAttack = true; break;
            }
        }

        if (!hasAttack)
        {
            int slotToFill = 0;
            for (int i = 0; i < 4; i++) { if (EnemySkills[i] == 0) { slotToFill = i; break; } }
            int fallback = SkillBook.GetFallbackMove(d.Type1);
            EnemySkills[slotToFill] = fallback > 0 ? fallback : 1;
        }

        // 자기 타입 공격기가 하나도 없으면 그 타입의 기본 기술을 넣습니다. (낮은 레벨의 관장/야생도 자기 타입 기술을 쓰도록)
        bool hasStab = EnemySkills.Any(id => SkillBook.GetSkill(id) is { } s && s.Power > 0 && (s.Type == d.Type1 || s.Type == d.Type2));
        int stabMove = SkillBook.GetFallbackMove(d.Type1);
        if (!hasStab && !EnemySkills.Contains(stabMove))
        {
            int slot = Array.IndexOf(EnemySkills, 0);
            if (slot < 0) slot = 3; // 꽉 찼으면 점수가 가장 낮은 마지막 칸을 바꿉니다.
            EnemySkills[slot] = stabMove;
        }
    }

    private SkillInfo ChooseEnemySkill()
    {
        if (_enemyLock.Forced != null) return _enemyLock.Forced; // 모으기 2턴째, 재충전, 고정 행동

        var enemyInfo = EnemyBattleInfo;
        var myInfo = MyBattleInfo;
        Random rand = new Random();

        var availableSkills = EnemySkills.Where(id => id != 0).Select(id => SkillBook.GetSkill(id))
            .Where(s => s != null && s.IsSupported && EnemyCanUseNow(s)).ToList();
        if (availableSkills.Count == 0) return SkillBook.GetSkill(1)!;

        SkillInfo bestSkill = availableSkills[0]!;
        int bestScore = -9999;

        foreach (var m in availableSkills)
        {
            int score = 0;
            if (m!.Category == SkillCategory.Status)
            {
                // 상태이상을 걸 수 있는 상황이면 40% 확률로 우선 사용, 걸 수 없으면 사용하지 않음
                bool canInflict = m.Ailment != SkillAilment.None && PlayerAilment == SkillAilment.None &&
                                  !AilmentHelper.IsImmune(m, myInfo);
                if (m.Ailment != SkillAilment.None) score = canInflict && rand.Next(100) < 40 ? 500 : -100;
                else if (m.HasStatChanges)
                {
                    // 능력치 변화: 아직 충분히 오르거나(자신) 내려가지(상대) 않았으면 30% 확률로 사용
                    var stages = m.StatTarget == StatTarget.User ? _enemyStages : _playerStages;
                    bool useful = m.StatChanges!.Any(c => c.Change > 0 ? stages[(int)c.Stat] < 2 : stages[(int)c.Stat] > -2);
                    score = useful && rand.Next(100) < 30 ? 300 : -100;
                }
                else score = -100;
            }
            else
            {
                double typeMult = BattleCalculator.TypeMultiplier(m.Type, myInfo);
                double stabMult = BattleCalculator.StabMultiplier(m.Type, enemyInfo);
                int enemyAtk = WildStat(m.Category == SkillCategory.Special ? enemyInfo?.BaseSpA : enemyInfo?.BaseAtk);
                int DamageAt(int power) => BattleCalculator.RawDamage(enemyAtk, m.Category == SkillCategory.Special ? CombatSpDef : CombatDef, typeMult, stabMult, BattleCalculator.BurnMultiplier(m.Category, EnemyAilment), crit: false, power);

                score = DamageAt(BattleCalculator.ExpectedPower(m));
                if (DamageAt(BattleCalculator.SurePower(m)) >= PlayerHp) score += 1000;

                int acc = m.Accuracy == 0 ? 100 : m.Accuracy;
                score = score * acc / 100;

                if (IsGymBattle)
                {
                    if (typeMult == 0) score = -9999;
                    else if (typeMult >= 2.0) score += 200;
                    else if (typeMult <= 0.5) score -= 50;
                }
            }

            if (!IsGymBattle) score += rand.Next(0, 10);
            if (score > bestScore) { bestScore = score; bestSkill = m; }
        }
        return bestSkill;
    }

    private async Task<bool> PlayerCanActThisTurnAsync()
    {
        if (PlayerAilment != SkillAilment.None)
        {
            if (_playerAilmentTurns > 0) _playerAilmentTurns--;
            bool canAct = AilmentHelper.CanActThisTurn(PlayerAilment, ref _playerAilmentTurns, Name, out string statusMsg, out bool cured);
            if (cured) PlayerAilment = SkillAilment.None;

            if (!string.IsNullOrEmpty(statusMsg))
            {
                BattleMessage = statusMsg;
                await Task.Delay(1500);
            }
            return canAct;
        }
        return true;
    }

    public async Task ExecuteTurnAsync(BattleAction playerAction)
    {
        if (!IsBattleOpen || PlayerHp <= 0 || EnemyHp <= 0 || !IsPlayerTurn) return;
        MarkBattleAttended();
        if (playerAction == BattleAction.Run && _playerTrapTurns > 0)
        {
            BattleMessage = L.F("battle.trapped", PlayerBattleName); // 턴을 쓰지 않고 다시 고릅니다.
            return;
        }
        IsPlayerTurn = false; Random rand = new Random();

        if (playerAction == BattleAction.Run)
        {
            BattleMessage = L.T("battle.ran");
            await Task.Delay(1000);
            CloseBattle();
            IsPlayerTurn = true;
            return;
        }

        bool playerDodged = false;
        if (playerAction == BattleAction.Dodge)
        {
            if (!await PlayerCanActThisTurnAsync())
            {
                await EnemyTurnAction(false);
                return;
            }

            if (rand.Next(100) < 70)
            {
                playerDodged = true; _isCounterReady = true;
                BattleMessage = L.T("battle.dodge.ready");
            }
            else BattleMessage = L.T("battle.dodge.fail");

            await Task.Delay(1500);
        }

        if (EnemyHp <= 0) { await CheckBattleEndAsync(); return; }
        await EnemyTurnAction(playerDodged);
    }

    // 기술 턴: 기술을 고르는 순간 상대도 기술을 정하고, 스피드로 먼저 움직일 쪽을 정합니다. (회피/아이템/포획/도망은 항상 먼저)
    public async Task ExecuteSkillTurnAsync(SkillInfo playerSkill)
    {
        if (!IsBattleOpen || PlayerHp <= 0 || EnemyHp <= 0 || !IsPlayerTurn) return;
        MarkBattleAttended();
        IsPlayerTurn = false; IsAttackMenuOpen = false;
        Random rand = new Random();

        SkillInfo enemySkill = ChooseEnemySkill();
        _playerChoice = playerSkill; _enemyChoice = enemySkill;
        if (DecidePlayerFirst(playerSkill, enemySkill, rand))
        {
            await PlayerActAsync(playerSkill, rand);
            await EnemyActAsync(enemySkill, playerDodged: false, rand);
        }
        else
        {
            await EnemyActAsync(enemySkill, playerDodged: false, rand);
            await PlayerActAsync(playerSkill, rand);
        }
        await FinishTurnAsync();
    }

    // 내 포켓몬의 기술 사용 (먼저 쓰러졌거나 상태이상으로 못 움직이면 아무것도 하지 않음)
    private async Task PlayerActAsync(SkillInfo playerSkill, Random rand)
    {
        if (!IsBattleOpen || PlayerHp <= 0 || EnemyHp <= 0) return;
        if (await RestIfRechargingAsync(userIsPlayer: true, PlayerBattleName)) return;

        bool canAct = await PlayerCanActThisTurnAsync();
        if (!canAct && !(playerSkill.NeedsUserAsleep && PlayerAilment == SkillAilment.Sleep))
        {
            ClearMoveLock(userIsPlayer: true);
            return;
        }
        if (_playerFlinched)
        {
            ClearMoveLock(userIsPlayer: true);
            BattleMessage = L.F("battle.flinched", PlayerBattleName);
            await Task.Delay(1500);
            return;
        }
        if (playerSkill.FailsIfHitFirst && _playerHitBy != null)
        {
            BattleMessage = L.F("battle.focusLost", PlayerBattleName);
            await Task.Delay(1500);
            return;
        }

        var myInfo = MyBattleInfo;
        var enemyInfo = EnemyBattleInfo;

        BattleMessage = L.F("battle.used", PlayerBattleName, playerSkill.Name);
        await Task.Delay(1000);

        bool wasFirstMove = !_playerHasMoved;
        _playerHasMoved = true;
        bool conditionMet = MoveConditionMet(playerSkill, userIsPlayer: true, wasFirstMove);
        _playerUsedMoves.Add(playerSkill.Id);
        if (!conditionMet)
        {
            ClearMoveLock(userIsPlayer: true);
            BattleMessage = L.T("battle.failed");
            await Task.Delay(1500);
            return;
        }

        if (await StartChargeAsync(playerSkill, userIsPlayer: true, PlayerBattleName)) return;
        AdvanceMoveLock(playerSkill, userIsPlayer: true, rand);
        await WakeByUproarAsync(playerSkill, userIsPlayer: true);

        bool isHit = !MissesHiddenTarget(playerSkill, _enemyLock) && RollHit(playerSkill, _playerStages, _enemyStages, rand);

        if (isHit && playerSkill.Power > 0)
        {
            bool special = playerSkill.Category == SkillCategory.Special;
            double typeMultiplier = BattleCalculator.TypeMultiplier(playerSkill.Type, enemyInfo);
            double stabMultiplier = BattleCalculator.StabMultiplier(playerSkill.Type, myInfo);

            // 상성 0배: 데미지도 상태이상/능력치 변화도 없습니다.
            if (typeMultiplier == 0)
            {
                _isCounterReady = false;
                ClearMoveLock(userIsPlayer: true);
                BattleMessage = L.F("battle.noEffectOn", EnemyBattleName);
                await Task.Delay(1500);
                return;
            }

            bool counter = _isCounterReady; // 회피 뒤 반격 2배는 이번 기술의 모든 타격에 적용
            _isCounterReady = false;

            int hits = BattleCalculator.RollHitCount(playerSkill, rand), landed = 0, dealt = 0;
            int attackAnim = AttackAnimationFor(playerSkill, hits);
            while (landed < hits && EnemyHp > 0)
            {
                bool crit = BattleCalculator.RollCrit(playerSkill, rand);
                double myCombatAtk = (special ? CombatSpAtk : CombatAtk) * AttackStage(_playerStages, special ? BattleStat.SpAttack : BattleStat.Attack, crit);
                double enemyCombatDef = WildStat(special ? enemyInfo?.BaseSpD : enemyInfo?.BaseDef);
                if (IsGymBattle) enemyCombatDef *= BattleCalculator.GYM_BONUS;
                enemyCombatDef *= DefenseStage(_enemyStages, special ? BattleStat.SpDefense : BattleStat.Defense, crit);

                int damage = BattleCalculator.RawDamage((int)myCombatAtk, (int)enemyCombatDef, typeMultiplier, stabMultiplier, BattleCalculator.BurnMultiplier(playerSkill.Category, PlayerAilment), crit, PowerOf(playerSkill, userIsPlayer: true));
                if (counter) damage *= 2;
                damage = Math.Max(1, damage);

                // 공격 모션 → 이펙트 → 체력 감소 순서 (연속 공격은 타격마다)
                PlayBattleMotion(attackAnim, 15);
                await PlaySkillFxAsync(playerSkill, targetIsEnemy: true);

                IsEnemyTakingDamage = true;
                dealt += Math.Min(damage, EnemyHp);
                EnemyHp = Math.Max(0, EnemyHp - damage);
                _enemyHitBy = playerSkill;
                landed++;

                BattleMessage = L.F("battle.damage.dealt", BattleCalculator.HitText(typeMultiplier, crit), damage);

                PlayHitMotion(targetIsEnemy: true);

                await Task.Delay(hits > 1 ? MULTI_HIT_SHAKE_MS : HIT_SHAKE_MS);
                IsEnemyTakingDamage = false;
                await Task.Delay(landed < hits && EnemyHp > 0 ? MULTI_HIT_GAP_MS : HIT_REST_MS);
            }

            await FinishHitsAsync(playerSkill, hits > 1 ? landed : 0, dealt, userIsPlayer: true, rand);
        }
        else if (isHit)
        {
            // 위력 0인 변화 기술 (전기자석파, 칼춤, 울음소리 등)
            PlayBattleMotion(PokemonAnim.ROTATE, 30);
            await UseStatusMoveAsync(playerSkill, userIsPlayer: true);
        }
        else
        {
            _isCounterReady = false;
            ClearMoveLock(userIsPlayer: true);
            BattleMessage = L.F("battle.missed", PlayerBattleName);
            await Task.Delay(1500);
        }
    }

    // 턴 마무리: 상태이상 데미지 → 승패 확인 → 다음 행동 선택
    private async Task FinishTurnAsync()
    {
        _playerFlinched = _enemyFlinched = false; // 풀죽음은 그 턴에만
        ClearTurnState();
        if (PlayerHp <= 0 || EnemyHp <= 0) { await CheckBattleEndAsync(); return; }

        await ProcessEnemyTurnEndAilment();
        await ProcessPlayerTurnEndAilment();
        await ProcessTrapDamageAsync(targetIsPlayer: false);
        await ProcessTrapDamageAsync(targetIsPlayer: true);

        if (PlayerHp <= 0 || EnemyHp <= 0) { await CheckBattleEndAsync(); return; }

        // 모으기 2턴째, 재충전, 고정 행동: 메뉴 없이 바로 다음 턴을 진행합니다.
        if (_playerLock.Forced != null)
        {
            IsPlayerTurn = true;
            await ExecuteSkillTurnAsync(_playerLock.Forced);
            return;
        }

        BattleMessage = L.T("battle.choose");
        IsPlayerTurn = true;
    }

    // 기술의 상태이상을 대상에게 걸어 봅니다. (확률, 이미 걸린 상태, 타입 면역 반영)
    // isStatusMove: 위력 0인 변화 기술이면 실패/무효 메시지도 보여줍니다.
    private async Task<bool> TryInflictAilmentAsync(SkillInfo skill, bool targetIsEnemy, bool isStatusMove)
    {
        string targetName = targetIsEnemy ? EnemyBattleName : Name;

        if ((targetIsEnemy ? EnemyHp : PlayerHp) <= 0) return false; // 쓰러진 쪽에는 걸리지 않음

        if (skill.Ailment == SkillAilment.None)
        {
            if (isStatusMove) { BattleMessage = L.T("battle.nothing"); await Task.Delay(1500); }
            return false;
        }

        var targetInfo = targetIsEnemy ? EnemyBattleInfo : MyBattleInfo; // 메가진화 중이면 메가 폼 타입
        bool alreadyAiled = (targetIsEnemy ? EnemyAilment : PlayerAilment) != SkillAilment.None
                            || (skill.Ailment == SkillAilment.Sleep && IsUproarActive); // 소란피기 중에는 아무도 잠들지 못함
        bool immune = AilmentHelper.IsImmune(skill, targetInfo);

        if (alreadyAiled || immune)
        {
            if (isStatusMove)
            {
                BattleMessage = immune ? L.F("battle.noEffectOn", targetName) : L.T("battle.failed");
                await Task.Delay(1500);
            }
            return false;
        }

        if (new Random().Next(100) >= skill.AilmentChance)
        {
            if (isStatusMove) { BattleMessage = L.T("battle.failed"); await Task.Delay(1500); }
            return false;
        }

        if (targetIsEnemy)
        {
            EnemyAilment = skill.Ailment;
            _enemyAilmentTurns = AilmentHelper.GetInitialTurns(EnemyAilment);
            _enemyBadPoisonTurnCount = 0;
        }
        else
        {
            PlayerAilment = skill.Ailment;
            _playerAilmentTurns = AilmentHelper.GetInitialTurns(PlayerAilment);
            _playerBadPoisonTurnCount = 0;
        }

        BattleMessage = L.F("battle.ailment.inflicted", targetName, AilmentHelper.GetAilmentIcon(skill.Ailment));
        if (targetIsEnemy) IsEnemyAilmentFlash = true; else IsPlayerAilmentFlash = true;
        await Task.Delay(1500);
        IsEnemyAilmentFlash = false; IsPlayerAilmentFlash = false;
        return true;
    }

    private async Task ProcessPlayerTurnEndAilment()
    {
        if (PlayerAilment != SkillAilment.None && PlayerHp > 0)
        {
            if (PlayerAilment == SkillAilment.BadPoison) _playerBadPoisonTurnCount++;
            int tickDamage = AilmentHelper.GetTurnEndDamage(PlayerAilment, PlayerMaxHp, _playerBadPoisonTurnCount, out string ailmentMsg);
            if (tickDamage > 0)
            {
                PlayerHp = Math.Max(0, PlayerHp - tickDamage);
                BattleMessage = L.F("battle.ailment.tick", Name, ailmentMsg, tickDamage);
                await Task.Delay(1500);
            }
        }
    }

    // 회피/아이템 뒤의 상대 차례 (회피·아이템은 항상 먼저 처리됩니다)
    private async Task EnemyTurnAction(bool playerDodged)
    {
        if (!IsBattleOpen || PlayerHp <= 0 || EnemyHp <= 0 || IsBattleResolved) return;
        await EnemyActAsync(ChooseEnemySkill(), playerDodged, new Random());
        await FinishTurnAsync();
    }

    // 상대 포켓몬의 기술 사용 (먼저 쓰러졌거나 상태이상으로 못 움직이면 아무것도 하지 않음)
    private async Task EnemyActAsync(SkillInfo enemySkill, bool playerDodged, Random rand)
    {
        if (!IsBattleOpen || PlayerHp <= 0 || EnemyHp <= 0 || IsBattleResolved) return;

        string enemyName = EnemyBattleName;
        if (await RestIfRechargingAsync(userIsPlayer: false, enemyName)) return;

        var myInfo = MyBattleInfo;
        var enemyInfo = EnemyBattleInfo;

        if (EnemyAilment != SkillAilment.None)
        {
            if (_enemyAilmentTurns > 0) _enemyAilmentTurns--;

            bool canAct = AilmentHelper.CanActThisTurn(EnemyAilment, ref _enemyAilmentTurns, EnemyBattleName, out string statusMsg, out bool cured);
            if (cured) EnemyAilment = SkillAilment.None;
            if (!string.IsNullOrEmpty(statusMsg))
            {
                BattleMessage = statusMsg;
                await Task.Delay(1500);
            }
            if (!canAct && !(enemySkill.NeedsUserAsleep && EnemyAilment == SkillAilment.Sleep))
            {
                ClearMoveLock(userIsPlayer: false);
                return;
            }
        }

        if (_enemyFlinched)
        {
            ClearMoveLock(userIsPlayer: false);
            BattleMessage = L.F("battle.flinched", enemyName);
            await Task.Delay(1500);
            return;
        }
        if (enemySkill.FailsIfHitFirst && _enemyHitBy != null)
        {
            BattleMessage = L.F("battle.focusLost", enemyName);
            await Task.Delay(1500);
            return;
        }

        BattleMessage = L.F("battle.used", enemyName, enemySkill.Name);
        await Task.Delay(1000);

        bool wasFirstMove = !_enemyHasMoved;
        _enemyHasMoved = true;
        bool conditionMet = MoveConditionMet(enemySkill, userIsPlayer: false, wasFirstMove);
        _enemyUsedMoves.Add(enemySkill.Id);
        if (!conditionMet)
        {
            ClearMoveLock(userIsPlayer: false);
            BattleMessage = L.T("battle.failed");
            await Task.Delay(1500);
            return;
        }

        if (await StartChargeAsync(enemySkill, userIsPlayer: false, enemyName)) return;
        AdvanceMoveLock(enemySkill, userIsPlayer: false, rand);
        await WakeByUproarAsync(enemySkill, userIsPlayer: false);

        // 자기 능력치만 올리는 기술(칼춤 등)은 회피와 상관없이 성공합니다.
        if (playerDodged && IsSelfTargetingStatusMove(enemySkill)) playerDodged = false;
        bool enemyHits = !MissesHiddenTarget(enemySkill, _playerLock) && RollHit(enemySkill, _enemyStages, _playerStages, rand);

        if (!playerDodged && enemyHits && enemySkill.Power > 0)
        {
            bool special = enemySkill.Category == SkillCategory.Special;
            double typeMultiplier = BattleCalculator.TypeMultiplier(enemySkill.Type, myInfo);
            double stabMultiplier = BattleCalculator.StabMultiplier(enemySkill.Type, enemyInfo);

            // 상성 0배: 데미지도 상태이상/능력치 변화도 없습니다.
            if (typeMultiplier == 0)
            {
                ClearMoveLock(userIsPlayer: false);
                BattleMessage = L.F("battle.noEffectOn", PlayerBattleName);
                await Task.Delay(1500);
                return;
            }

            int hits = BattleCalculator.RollHitCount(enemySkill, rand), landed = 0, dealt = 0;
            int attackAnim = AttackAnimationFor(enemySkill, hits);
            while (landed < hits && PlayerHp > 0)
            {
                bool crit = BattleCalculator.RollCrit(enemySkill, rand);
                double enemyCombatAtk = WildStat(special ? enemyInfo?.BaseSpA : enemyInfo?.BaseAtk);
                if (IsGymBattle) enemyCombatAtk *= BattleCalculator.GYM_BONUS;
                enemyCombatAtk *= AttackStage(_enemyStages, special ? BattleStat.SpAttack : BattleStat.Attack, crit);
                double myCombatDef = (special ? CombatSpDef : CombatDef) * DefenseStage(_playerStages, special ? BattleStat.SpDefense : BattleStat.Defense, crit);

                int enemyDamage = Math.Max(1, BattleCalculator.RawDamage((int)enemyCombatAtk, (int)myCombatDef, typeMultiplier, stabMultiplier, BattleCalculator.BurnMultiplier(enemySkill.Category, EnemyAilment), crit, PowerOf(enemySkill, userIsPlayer: false)));

                // 공격 모션 → 이펙트 → 체력 감소 순서 (연속 공격은 타격마다)
                _enemyTempActionId = attackAnim; _enemyTempActionTimer = 15; UpdateEnemyAnimation(attackAnim);
                await PlaySkillFxAsync(enemySkill, targetIsEnemy: false);

                IsPlayerTakingDamage = true;
                dealt += Math.Min(enemyDamage, PlayerHp);
                PlayerHp = Math.Max(0, PlayerHp - enemyDamage);
                _playerHitBy = enemySkill;
                landed++;

                BattleMessage = L.F("battle.damage.taken", BattleCalculator.HitText(typeMultiplier, crit), PlayerBattleName, enemyDamage);

                PlayHitMotion(targetIsEnemy: false);

                await Task.Delay(hits > 1 ? MULTI_HIT_SHAKE_MS : HIT_SHAKE_MS);
                IsPlayerTakingDamage = false;
                await Task.Delay(landed < hits && PlayerHp > 0 ? MULTI_HIT_GAP_MS : HIT_REST_MS);
            }

            await FinishHitsAsync(enemySkill, hits > 1 ? landed : 0, dealt, userIsPlayer: false, rand);
        }
        else if (!playerDodged && enemyHits)
        {
            // 위력 0인 변화 기술
            _enemyTempActionId = PokemonAnim.ROTATE; _enemyTempActionTimer = 30; UpdateEnemyAnimation(PokemonAnim.ROTATE);
            await UseStatusMoveAsync(enemySkill, userIsPlayer: false);
        }
        else if (playerDodged) { ClearMoveLock(userIsPlayer: false); BattleMessage = L.F("battle.dodged", PlayerBattleName); await Task.Delay(1500); }
        else { ClearMoveLock(userIsPlayer: false); BattleMessage = L.F("battle.missed", enemyName); await Task.Delay(1500); }
    }

    private async Task ProcessEnemyTurnEndAilment()
    {
        if (EnemyAilment != SkillAilment.None && EnemyHp > 0)
        {
            if (EnemyAilment == SkillAilment.BadPoison) _enemyBadPoisonTurnCount++;
            int tickDamage = AilmentHelper.GetTurnEndDamage(EnemyAilment, EnemyMaxHp, _enemyBadPoisonTurnCount, out string ailmentMsg);
            if (tickDamage > 0)
            {
                EnemyHp = Math.Max(0, EnemyHp - tickDamage);
                BattleMessage = L.F("battle.ailment.tick", EnemyBattleName, ailmentMsg, tickDamage);
                await Task.Delay(1500);
            }
        }
    }

    public void CheckAndLearnSkillAfterVictory()
    {
        var activeMoves = SkillBook.GetActiveLearnset(SpeciesId);

        if (activeMoves.Count == 0)
        {
            ProceedToCatchOrEnd();
            return;
        }

        var availableSkills = activeMoves
            .Where(m => m.Level <= Level && !KnowsSkill(m.MoveId))
            .Select(m => new { m.Level, Skill = SkillBook.GetSkill(m.MoveId) })
            .Where(x => x.Skill != null && x.Skill.IsSupported)
            .ToList();

        if (availableSkills.Count > 0)
        {
            Random rand = new Random();
            var picked = availableSkills.OrderByDescending(x => x.Level).ThenBy(x => rand.Next()).Take(2).Select(x => x.Skill!).ToList();

            RecommendedSkill1 = picked.Count > 0 ? picked[0] : null;
            RecommendedSkill2 = picked.Count > 1 ? picked[1] : null;

            IsSkillLearnMenuOpen = true;
            BattleMessage = L.T("battle.newSkill");
        }
        else
        {
            ProceedToCatchOrEnd();
        }
    }

    // [시퀀스 4-C] 야생에서 도망가기
    public async void LeaveWildBattle()
    {
        IsCatchOffered = false;
        BattleMessage = L.F("battle.leave", EnemyName);
        await Task.Delay(1500);

        // 도망 후 야생 보상으로 이동
        await ProcessWildBattleRewardsAsync();
    }


    // 능력치 단계: 배틀 중에만 유지되며 -6 ~ +6 (원작 규칙). 배틀을 시작할 때마다 0으로 초기화합니다.
    private const int MAX_STAT_STAGE = 6;
    private readonly int[] _playerStages = new int[Enum.GetValues<BattleStat>().Length];
    private readonly int[] _enemyStages = new int[Enum.GetValues<BattleStat>().Length];

    // 선공: 스피드가 높은 쪽이 먼저, 같으면 50:50. 느린 쪽도 이 확률(%)로 먼저 움직입니다. 마비면 스피드 절반.
    private const int SLOW_SIDE_FIRST_CHANCE = 20;

    // 타격 연출 시간(ms)
    private const int HIT_SHAKE_MS = 600, HIT_REST_MS = 900;
    private const int MULTI_HIT_SHAKE_MS = 400, MULTI_HIT_GAP_MS = 150;

    // 풀죽음: 먼저 움직인 쪽의 기술에 맞으면 그 턴에 행동하지 못합니다.
    private bool _playerFlinched, _enemyFlinched;
    // 이번 배틀에서 기술을 쓴 적이 있는지 (속이기처럼 처음에만 성공하는 기술용)
    private bool _playerHasMoved, _enemyHasMoved;

    private void ResetStatStages()
    {
        Array.Clear(_playerStages);
        Array.Clear(_enemyStages);
        _playerFlinched = _enemyFlinched = false;
        _playerHasMoved = _enemyHasMoved = false;
        ClearTurnState();
        ClearMoveLock(userIsPlayer: true);
        ClearMoveLock(userIsPlayer: false);
        _playerUsedMoves.Clear(); _enemyUsedMoves.Clear();
        _playerLostTypes.Clear(); _enemyLostTypes.Clear();
        _playerTrapTurns = _enemyTrapTurns = 0;
        _playerTrapSkill = _enemyTrapSkill = null;
    }

    // 여러 턴에 걸친 기술의 진행 상태
    private sealed class MoveLock
    {
        public SkillInfo? Forced;  // 다음 턴에 자동으로 나가는 기술 (모으기 2턴째, 재충전, 고정 행동)
        public bool Recharge;      // 다음 턴은 쉼
        public int LockTurns;      // 고정 행동의 남은 턴
        public SkillInfo? LockSkill; // 고정 행동 중인 기술 (마지막 턴이 끝날 때까지 유지)
        public int RollCount;      // 구르기 등을 연속으로 맞힌 횟수
        public bool Hidden;        // 공중날기 등으로 숨어 있어 공격에 맞지 않음
    }

    private const int ROLLING_TURNS = 5, UPROAR_TURNS = 3;
    private bool IsUproarActive => _playerLock.LockSkill?.IsUproar == true || _enemyLock.LockSkill?.IsUproar == true;

    // 구르기, 아이스볼: 맞힐 때마다 위력 2배 (30 → 60 → 120 → 240 → 480)
    private int PowerOf(SkillInfo skill, bool userIsPlayer) =>
        skill.IsRolling ? skill.Power << Math.Min(LockOf(userIsPlayer).RollCount, ROLLING_TURNS - 1) : skill.Power;

    // 묶기 기술 상태: 남은 턴과 묶은 기술 (묶인 쪽 기준)
    private const int TRAP_DAMAGE_DIVISOR = 8;
    private int _playerTrapTurns, _enemyTrapTurns;
    private SkillInfo? _playerTrapSkill, _enemyTrapSkill;

    // 턴이 끝날 때 묶인 쪽이 데미지를 받고, 턴이 다 되면 풀려납니다.
    private async Task ProcessTrapDamageAsync(bool targetIsPlayer)
    {
        int turns = targetIsPlayer ? _playerTrapTurns : _enemyTrapTurns;
        int hp = targetIsPlayer ? PlayerHp : EnemyHp;
        if (turns <= 0 || hp <= 0) return;

        int maxHp = targetIsPlayer ? PlayerMaxHp : EnemyMaxHp;
        string who = targetIsPlayer ? PlayerBattleName : EnemyBattleName;
        string moveName = (targetIsPlayer ? _playerTrapSkill : _enemyTrapSkill)?.Name ?? "";
        int damage = Math.Max(1, maxHp / TRAP_DAMAGE_DIVISOR);

        if (targetIsPlayer) { PlayerHp = Math.Max(0, hp - damage); IsPlayerTakingDamage = true; }
        else { EnemyHp = Math.Max(0, hp - damage); IsEnemyTakingDamage = true; }
        BattleMessage = L.F("battle.trapTick", who, moveName, damage);
        await Task.Delay(HIT_SHAKE_MS);
        IsPlayerTakingDamage = false; IsEnemyTakingDamage = false;
        await Task.Delay(HIT_REST_MS);

        turns--;
        if (targetIsPlayer) _playerTrapTurns = turns; else _enemyTrapTurns = turns;
        if (turns == 0 && (targetIsPlayer ? PlayerHp : EnemyHp) > 0)
        {
            BattleMessage = L.F("battle.trapFree", who, moveName);
            await Task.Delay(1000);
        }
    }

    // 소란피기: 맞는 쪽이 잠들어 있으면 깨웁니다.
    private async Task WakeByUproarAsync(SkillInfo skill, bool userIsPlayer)
    {
        if (!skill.IsUproar) return;
        bool targetIsPlayer = !userIsPlayer;
        if ((targetIsPlayer ? PlayerAilment : EnemyAilment) != SkillAilment.Sleep) return;

        if (targetIsPlayer) { PlayerAilment = SkillAilment.None; _playerAilmentTurns = 0; }
        else { EnemyAilment = SkillAilment.None; _enemyAilmentTurns = 0; }
        BattleMessage = L.F("battle.uproarWake", targetIsPlayer ? PlayerBattleName : EnemyBattleName);
        await Task.Delay(1000);
    }
    private readonly MoveLock _playerLock = new(), _enemyLock = new();
    private MoveLock LockOf(bool userIsPlayer) => userIsPlayer ? _playerLock : _enemyLock;

    private void SetHidden(bool userIsPlayer, bool hidden)
    {
        LockOf(userIsPlayer).Hidden = hidden;
        if (userIsPlayer) IsPlayerVisible = !hidden; else IsEnemyVisible = !hidden;
    }

    // 잠듦, 풀죽음, 빗나감 등으로 끊기면 진행 중이던 기술도 끝납니다.
    private void ClearMoveLock(bool userIsPlayer)
    {
        var state = LockOf(userIsPlayer);
        state.Forced = null; state.Recharge = false; state.LockTurns = 0; state.LockSkill = null; state.RollCount = 0;
        SetHidden(userIsPlayer, false);
    }

    // 재충전 턴이면 쉬고 true
    private async Task<bool> RestIfRechargingAsync(bool userIsPlayer, string who)
    {
        if (!LockOf(userIsPlayer).Recharge) return false;
        ClearMoveLock(userIsPlayer);
        BattleMessage = L.F("battle.recharge", who);
        await Task.Delay(1500);
        return true;
    }

    private const int CHARGE_ANIM_TICKS = 40; // 모으는 메시지가 보이는 1.3초 동안 (33ms 단위)

    // 공격 모션: 연속 공격은 연속 모션, 물리는 공격, 특수는 발사 (대부분의 스프라이트에 있는 모션만 씁니다)
    private static int AttackAnimationFor(SkillInfo skill, int hits) =>
        hits > 1 ? PokemonAnim.DOUBLE : skill.Category == SkillCategory.Special ? PokemonAnim.SHOOT : PokemonAnim.ATTACK;

    // 모으기/숨기 기술의 첫 턴이면 준비만 하고 true, 둘째 턴이면 상태를 풀고 false
    private async Task<bool> StartChargeAsync(SkillInfo skill, bool userIsPlayer, string who)
    {
        if (!skill.IsCharging && !skill.IsHiding) return false;

        var state = LockOf(userIsPlayer);
        if (state.Forced?.Id == skill.Id)
        {
            ClearMoveLock(userIsPlayer);
            return false;
        }

        state.Forced = skill;
        if (skill.IsHiding) SetHidden(userIsPlayer, true);
        else if (userIsPlayer) { PlayBattleMotion(PokemonAnim.CHARGE, CHARGE_ANIM_TICKS); }
        else { _enemyTempActionTimer = CHARGE_ANIM_TICKS; UpdateEnemyAnimation(PokemonAnim.CHARGE); }
        BattleMessage = L.F(skill.IsHiding ? "battle.hiding" : "battle.charging", who);
        await Task.WhenAll(Task.Delay(1300), skill.IsCharging ? PlayChargeFxAsync(skill, userIsPlayer) : Task.CompletedTask);
        return true;
    }

    // 고정 행동: 처음 쓸 때 턴 수를 정하고, 턴이 남아 있으면 다음 턴에도 자동으로 나갑니다.
    private void AdvanceMoveLock(SkillInfo skill, bool userIsPlayer, Random rand)
    {
        if (!skill.IsLocking) return;
        var state = LockOf(userIsPlayer);
        if (state.LockTurns == 0)
        {
            state.LockTurns = skill.IsRolling ? ROLLING_TURNS : skill.IsUproar ? UPROAR_TURNS : rand.Next(2, 4);
            state.LockSkill = skill;
            state.RollCount = 0;
        }
        state.LockTurns--;
        state.Forced = state.LockTurns > 0 ? skill : null;
    }

    // 맞는 쪽이 숨어 있으면 빗나갑니다. (자기 능력치만 올리는 기술은 상관없음)
    private static bool MissesHiddenTarget(SkillInfo skill, MoveLock target) => target.Hidden && !IsSelfTargetingStatusMove(skill);

    // 이번 배틀에서 쓴 기술 (비장의무기)과 잃은 타입 (불사르기, 전광쌍격)
    private readonly HashSet<int> _playerUsedMoves = new(), _enemyUsedMoves = new();
    private readonly List<PokemonType> _playerLostTypes = new(), _enemyLostTypes = new();

    // 폼 타입에서 잃은 타입을 뺀 배틀용 정보 (상성과 자속 계산에 쓰입니다)
    private static PokemonInfo? WithBattleTypes(PokemonInfo? info, PokemonType type1, PokemonType type2, List<PokemonType> lost)
    {
        if (info == null) return null;
        if (lost.Contains(type1)) type1 = PokemonType.None;
        if (lost.Contains(type2)) type2 = PokemonType.None;
        if (info.Type1 == type1 && info.Type2 == type2) return info;
        return new PokemonInfo
        {
            Id = info.Id, BaseHp = info.BaseHp, BaseAtk = info.BaseAtk, BaseDef = info.BaseDef, BaseSpeed = info.BaseSpeed, BaseSpA = info.BaseSpA, BaseSpD = info.BaseSpD,
            Type1 = type1,
            Type2 = type2,
        };
    }

    private PokemonInfo? EnemyBattleInfo
    {
        get
        {
            var (type1, type2) = Dex.GetFormTypes(EnemySpeciesId, _enemyForm);
            return WithBattleTypes(Dex.Get(EnemySpeciesId), type1, type2, _enemyLostTypes);
        }
    }

    private static bool HasType(PokemonInfo? info, PokemonType type) => info != null && type != PokemonType.None && (info.Type1 == type || info.Type2 == type);

    // 비장의무기: 알고 있는 다른 기술이 있고, 그 기술을 모두 써 봤을 때만
    private bool HasUsedOtherMoves(SkillInfo skill, bool userIsPlayer)
    {
        var known = (userIsPlayer ? Skills : EnemySkills).Where(id => id != 0 && id != skill.Id).ToList();
        var used = userIsPlayer ? _playerUsedMoves : _enemyUsedMoves;
        return known.Count > 0 && known.All(used.Contains);
    }

    // 이번 턴 상대의 행동과 상관없이 정해지는 조건
    private bool MoveStateAllows(SkillInfo skill, bool userIsPlayer, bool wasFirstMove)
    {
        SkillAilment userAilment = userIsPlayer ? PlayerAilment : EnemyAilment;
        SkillAilment targetAilment = userIsPlayer ? EnemyAilment : PlayerAilment;
        PokemonInfo? userInfo = userIsPlayer ? MyBattleInfo : EnemyBattleInfo;
        PokemonInfo? targetInfo = userIsPlayer ? EnemyBattleInfo : MyBattleInfo;

        if (skill.IsUnavailable) return false;
        if (skill.IsFirstTurnOnly && !wasFirstMove) return false;
        if (skill.NeedsTargetAsleep && targetAilment != SkillAilment.Sleep) return false;
        if (skill.NeedsUserAsleep && userAilment != SkillAilment.Sleep) return false;
        if (skill.NeedsSharedType && !(HasType(targetInfo, userInfo?.Type1 ?? PokemonType.None) || HasType(targetInfo, userInfo?.Type2 ?? PokemonType.None))) return false;
        if (skill.LosesOwnType && !HasType(userInfo, skill.Type)) return false;
        if (skill.IsLastResort && !HasUsedOtherMoves(skill, userIsPlayer)) return false;
        return true;
    }

    // 이번 턴에 고른 기술 (회피/아이템 등 기술을 안 쓴 턴은 null)과 이번 턴에 맞은 기술
    private SkillInfo? _playerChoice, _enemyChoice, _playerHitBy, _enemyHitBy;

    private void ClearTurnState()
    {
        _playerChoice = _enemyChoice = _playerHitBy = _enemyHitBy = null;
        if (_playerLock.LockTurns == 0) _playerLock.LockSkill = null; // 고정 행동은 마지막 턴이 끝날 때 해제
        if (_enemyLock.LockTurns == 0) _enemyLock.LockSkill = null;
    }

    // 조건이 맞지 않아 실패하는 기술인지 (쓴 쪽, 상대의 상태로 판단)
    private bool MoveConditionMet(SkillInfo skill, bool userIsPlayer, bool wasFirstMove)
    {
        SkillInfo? targetChoice = userIsPlayer ? _enemyChoice : _playerChoice;
        SkillInfo? hitBy = userIsPlayer ? _playerHitBy : _enemyHitBy;

        if (!MoveStateAllows(skill, userIsPlayer, wasFirstMove)) return false;
        if (skill.NeedsPhysicalHitFirst && hitBy?.Category != SkillCategory.Physical) return false;
        if (skill.NeedsTargetAttacking && (targetChoice == null || targetChoice.Power <= 0)) return false;
        return true;
    }

    // 상대 AI: 지금 쓰면 반드시 실패하는 기술은 고르지 않습니다. (상대가 뭘 고를지는 모르므로 기습/트랩셸은 후보에 둡니다)
    private bool EnemyCanUseNow(SkillInfo skill) => MoveStateAllows(skill, userIsPlayer: false, wasFirstMove: !_enemyHasMoved);

    // 급소는 공격 쪽의 낮아진 단계와 방어 쪽의 높아진 단계를 무시합니다. (원작 규칙)
    private static double AttackStage(int[] stages, BattleStat stat, bool crit) =>
        StageMultiplier(crit ? Math.Max(0, stages[(int)stat]) : stages[(int)stat]);

    private static double DefenseStage(int[] stages, BattleStat stat, bool crit) =>
        StageMultiplier(crit ? Math.Min(0, stages[(int)stat]) : stages[(int)stat]);

    // 타격이 끝난 뒤의 효과: 연속 공격 횟수 안내 → 흡수/반동 → 상태이상 → 능력치 변화 → 풀죽음
    // hitCount: 연속 공격이면 맞힌 횟수, 아니면 0. dealt: 실제로 깎은 체력 합계
    private async Task FinishHitsAsync(SkillInfo skill, int hitCount, int dealt, bool userIsPlayer, Random rand)
    {
        if (hitCount > 0)
        {
            BattleMessage = L.F("battle.multiHit", hitCount);
            await Task.Delay(1000);
        }

        if (skill.NeedsRecharge && dealt > 0)
        {
            var state = LockOf(userIsPlayer);
            state.Recharge = true; state.Forced = skill;
        }
        if (skill.LosesOwnType && dealt > 0)
        {
            (userIsPlayer ? _playerLostTypes : _enemyLostTypes).Add(skill.Type);
        }
        if (skill.IsRolling && dealt > 0) LockOf(userIsPlayer).RollCount++;
        if (skill.IsTrapping && dealt > 0 && (userIsPlayer ? EnemyHp : PlayerHp) > 0 && (userIsPlayer ? _enemyTrapTurns : _playerTrapTurns) == 0)
        {
            int turns = rand.Next(4, 6);
            if (userIsPlayer) { _enemyTrapTurns = turns; _enemyTrapSkill = skill; }
            else { _playerTrapTurns = turns; _playerTrapSkill = skill; }
            BattleMessage = L.F("battle.trapStart", userIsPlayer ? EnemyBattleName : PlayerBattleName, skill.Name);
            await Task.Delay(1000);
        }

        await ApplyDrainAsync(skill, dealt, userIsPlayer);
        await TryInflictAilmentAsync(skill, targetIsEnemy: userIsPlayer, isStatusMove: false);
        await ApplyStatChangesAsync(skill, userIsPlayer, isStatusMove: false);

        if (rand.Next(100) < skill.FlinchChance)
        {
            if (userIsPlayer) _enemyFlinched = true; else _playerFlinched = true;
        }
    }

    // 흡수(입힌 데미지의 %만큼 회복) / 반동(입힌 데미지의 %만큼 자신도 데미지)
    private async Task ApplyDrainAsync(SkillInfo skill, int dealt, bool userIsPlayer)
    {
        if (skill.Drain == 0 || dealt <= 0) return;

        int hp = userIsPlayer ? PlayerHp : EnemyHp;
        int maxHp = userIsPlayer ? PlayerMaxHp : EnemyMaxHp;
        string who = userIsPlayer ? PlayerBattleName : EnemyBattleName;
        int amount = Math.Max(1, dealt * Math.Abs(skill.Drain) / 100);

        if (skill.Drain > 0)
        {
            if (hp <= 0 || hp >= maxHp) return;
            amount = Math.Min(amount, maxHp - hp);
            if (userIsPlayer) PlayerHp = hp + amount; else EnemyHp = hp + amount;
            BattleMessage = L.F("battle.drained", who, amount);
            await Task.Delay(1300);
            return;
        }

        if (userIsPlayer) { IsPlayerTakingDamage = true; PlayerHp = Math.Max(0, hp - amount); }
        else { IsEnemyTakingDamage = true; EnemyHp = Math.Max(0, hp - amount); }
        BattleMessage = L.F("battle.recoil", who, amount);
        await Task.Delay(HIT_SHAKE_MS);
        IsPlayerTakingDamage = false; IsEnemyTakingDamage = false;
        await Task.Delay(HIT_REST_MS);
    }

    // 공격/방어/특공/특방/스피드: +n이면 (2+n)/2배, -n이면 2/(2+n)배
    private static double StageMultiplier(int stage) => stage >= 0 ? (2 + stage) / 2.0 : 2.0 / (2 - stage);

    // 명중/회피: +n이면 (3+n)/3배, -n이면 3/(3+n)배
    private static double AccuracyStageMultiplier(int stage) => stage >= 0 ? (3 + stage) / 3.0 : 3.0 / (3 - stage);

    private static double Stage(int[] stages, BattleStat stat) => StageMultiplier(stages[(int)stat]);

    // 명중 판정: 기술 명중률 × (쓰는 쪽 명중 단계 - 맞는 쪽 회피 단계). 명중률이 없는 기술과 자신에게 쓰는 기술은 항상 성공
    private static bool RollHit(SkillInfo skill, int[] attackerStages, int[] defenderStages, Random rand)
    {
        if (skill.Accuracy == 0 || IsSelfTargetingStatusMove(skill)) return true;
        int stage = Math.Clamp(attackerStages[(int)BattleStat.Accuracy] - defenderStages[(int)BattleStat.Evasion], -MAX_STAT_STAGE, MAX_STAT_STAGE);
        return rand.NextDouble() * 100 < skill.Accuracy * AccuracyStageMultiplier(stage);
    }

    // 칼춤, 고속이동처럼 자기 능력치만 올리는 변화 기술 (회피로 피할 수 없고 빗나가지 않음)
    private static bool IsSelfTargetingStatusMove(SkillInfo skill) =>
        skill.Power == 0 && skill.HasStatChanges && skill.StatTarget == StatTarget.User && skill.Ailment == SkillAilment.None;

    private double PlayerBattleSpeed =>
        CombatSpeed * Stage(_playerStages, BattleStat.Speed) * (PlayerAilment == SkillAilment.Paralysis ? 0.5 : 1.0);

    private double EnemyBattleSpeed
    {
        get
        {
            double speed = WildStat(Dex.Get(EnemySpeciesId)?.BaseSpeed);
            if (IsGymBattle) speed *= BattleCalculator.GYM_BONUS;
            return speed * Stage(_enemyStages, BattleStat.Speed) * (EnemyAilment == SkillAilment.Paralysis ? 0.5 : 1.0);
        }
    }

    private bool DecidePlayerFirst(SkillInfo playerSkill, SkillInfo enemySkill, Random rand)
    {
        if (playerSkill.Priority != enemySkill.Priority) return playerSkill.Priority > enemySkill.Priority; // 우선도가 높으면 무조건 먼저
        double me = PlayerBattleSpeed, foe = EnemyBattleSpeed;
        if (me == foe) return rand.Next(2) == 0;
        bool slowerGoesFirst = rand.Next(100) < SLOW_SIDE_FIRST_CHANCE;
        return (me > foe) != slowerGoesFirst;
    }

    // 기술의 능력치 변화를 적용하고 메시지를 보여줍니다.
    // userIsPlayer: 기술을 쓴 쪽이 내 포켓몬인지. isStatusMove: 변화 기술이면 항상, 공격 기술이면 StatChance 확률로 적용
    private async Task ApplyStatChangesAsync(SkillInfo skill, bool userIsPlayer, bool isStatusMove)
    {
        if (!skill.HasStatChanges) return;
        if (!isStatusMove && new Random().Next(100) >= (skill.StatChance > 0 ? skill.StatChance : 100)) return;

        bool targetIsPlayer = (skill.StatTarget == StatTarget.User) == userIsPlayer;
        if (targetIsPlayer ? PlayerHp <= 0 : EnemyHp <= 0) return; // 쓰러진 쪽은 변하지 않음

        int[] stages = targetIsPlayer ? _playerStages : _enemyStages;
        string who = targetIsPlayer ? PlayerBattleName : EnemyBattleName;

        // 원시의힘처럼 여러 능력치가 한꺼번에 오르면 한 문장으로 묶어서 보여줍니다.
        var results = skill.StatChanges!.Select(c =>
        {
            int before = stages[(int)c.Stat];
            stages[(int)c.Stat] = Math.Clamp(before + c.Change, -MAX_STAT_STAGE, MAX_STAT_STAGE);
            string key = stages[(int)c.Stat] == before ? (c.Change > 0 ? "battle.stat.noHigher" : "battle.stat.noLower")
                       : c.Change >= 2 ? "battle.stat.sharplyRose"
                       : c.Change > 0 ? "battle.stat.rose"
                       : c.Change <= -2 ? "battle.stat.harshlyFell"
                       : "battle.stat.fell";
            return (Key: key, Stat: c.Stat);
        }).ToList();

        foreach (var group in results.GroupBy(r => r.Key))
        {
            string stats = string.Join(", ", group.Select(r => L.T($"battle.stat.{r.Stat}")));
            bool topic = group.Key is "battle.stat.noHigher" or "battle.stat.noLower";
            string josa = topic ? L.Josa(stats, "은", "는") : L.Josa(stats, "이", "가"); // 한국어 문구의 {2} (영어는 사용 안 함)
            BattleMessage = L.F(group.Key, who, stats, josa);
            if (!topic) await PlayStatFxAsync(rose: group.Key is "battle.stat.sharplyRose" or "battle.stat.rose", targetIsEnemy: !targetIsPlayer);
            await Task.Delay(1300);
        }
    }

    // 위력 0 변화 기술: 능력치 변화 / 상태이상 (둘 다 없는 기술은 "아무 일도 일어나지 않았다")
    private async Task UseStatusMoveAsync(SkillInfo skill, bool userIsPlayer)
    {
        if (skill.HasStatChanges) await ApplyStatChangesAsync(skill, userIsPlayer, isStatusMove: true);
        if (skill.Ailment != SkillAilment.None) await PlaySkillFxAsync(skill, targetIsEnemy: userIsPlayer);
        if (skill.Ailment != SkillAilment.None || !skill.HasStatChanges)
            await TryInflictAilmentAsync(skill, targetIsEnemy: userIsPlayer, isStatusMove: true);
    }

    // 올바른 레벨업 디자인과 밸런스가 적용된 스킬 학습 시스템
    public void RelearnFromLevel()
    {
        for (int i = 0; i < 4; i++) Skills[i] = 0;

        if (IsEgg)
        {
            return;
        }

        var d = Dex.Get(SpeciesId);
        if (d == null) return;

        int lvl = Level;
        int n = SkillBook.GetLearnCount(SpeciesId);

        if (n == 0)
        {
            Skills[0] = 1; // 몸통박치기
            int fallbackMove = SkillBook.GetFallbackMove(d.Type1);
            if (fallbackMove != 1) Skills[1] = fallbackMove;
            return;
        }

        int[] score = new int[4] { 0, 0, 0, 0 };

        // 1패스는 자력기(레벨업), 2패스는 TM(기술머신)으로 분리하여 순차적으로 채웁니다.
        for (int pass = 0; pass < 2; pass++)
        {
            bool tmPass = (pass == 1);
            for (int i = 0; i < n; i++)
            {
                int at = SkillBook.GetLearnLevel(SpeciesId, i);

                if (tmPass != (at == 0)) continue;
                if (!tmPass && at > lvl) continue;
                if (tmPass && lvl < 30) continue; // 강력한 TM은 30레벨 이상

                int mv = SkillBook.GetLearnMove(SpeciesId, i);
                if (mv == 0 || KnowsSkill(mv)) continue; // 🌟 MoveTable.Length 검사 삭제!

                var m = SkillBook.GetSkill(mv);
                if (m == null || !m.IsSupported) continue; // 도감에 없거나 이 게임에서 효과가 없는 기술(일격필살 등)은 제외

                int sc = (m.Category == SkillCategory.Status) ? 10 : m.Power + 20;

                if (m.Category != SkillCategory.Status && (m.Type == d.Type1 || m.Type == d.Type2)) sc += 40;

                sc += (at == 0 ? 50 : at);

                int slot = -1;
                for (int s = 0; s < 4; s++)
                {
                    if (sc > score[s]) { slot = s; break; }
                }
                if (slot < 0) continue;

                for (int s = 3; s > slot; s--)
                {
                    score[s] = score[s - 1];
                    Skills[s] = Skills[s - 1];
                }
                score[slot] = sc;
                Skills[slot] = mv;
            }
        }

        // 만약 공격 스킬이 하나도 없다면 강제 배정
        bool hasAttack = false;
        for (int i = 0; i < 4; i++)
        {
            if (Skills[i] != 0 && SkillBook.GetSkill(Skills[i])?.Power > 0)
            {
                hasAttack = true; break;
            }
        }

        if (!hasAttack)
        {
            int slotToFill = 0;
            for (int i = 0; i < 4; i++)
            {
                if (Skills[i] == 0) { slotToFill = i; break; }
            }

            int fallback = SkillBook.GetFallbackMove(d.Type1);
            Skills[slotToFill] = fallback > 0 ? fallback : 1;

            if (slotToFill < 3 && !KnowsSkill(1)) Skills[slotToFill + 1] = 1;
        }
    }

    public void SelectRecommendedSkill(int optionNumber)
    {
        _skillToLearn = (optionNumber == 1) ? RecommendedSkill1 : RecommendedSkill2;
        if (_skillToLearn == null) return;

        if (SkillCount < 4)
        {
            for (int i = 0; i < 4; i++)
            {
                if (Skills[i] == 0) { Skills[i] = _skillToLearn.Id; break; }
            }
            FinishSkillLearning(L.F("skill.learned", _skillToLearn.Name));
        }
        else
        {
            IsSkillLearnMenuOpen = false;
            IsSkillReplaceMenuOpen = true;
            BattleMessage = L.F("skill.full", _skillToLearn.Name);
        }
    }

    public void ReplaceExistingSkill(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex > 3 || _skillToLearn == null) return;

        var oldSkill = SkillBook.GetSkill(Skills[slotIndex]);
        string oldSkillName = oldSkill?.Name ?? L.T("skill.someMove");

        Skills[slotIndex] = _skillToLearn.Id;

        FinishSkillLearning(L.F("skill.replaced", oldSkillName, _skillToLearn.Name));
    }

    public void SkipSkillLearning()
    {
        FinishSkillLearning(L.T("skill.gaveUp"));
    }

    private async void FinishSkillLearning(string finalMessage)
    {
        IsSkillLearnMenuOpen = false;
        IsSkillReplaceMenuOpen = false;

        BattleMessage = finalMessage;
        await Task.Delay(2000);

        ProceedToCatchOrEnd();
    }
}
