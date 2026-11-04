#if DEBUG
namespace TamaPoke.Web.Game;

// 로컬 테스트 전용 세이브. Debug 빌드에만 들어가고 배포되는 Release 빌드에는 없습니다.
// 강한 포켓몬 6마리, 메가링과 모든 메가스톤, 넉넉한 아이템으로 시작합니다.
public static class TestSave
{
    private const int MAX_LEVEL_MINUTES = 99 * 60; // Lv.100
    private const int MAX_GENE = 31;
    private const int TRAINED_STAT = 50;
    private const int ITEM_COUNT = 99;
    private const int LEADER_SPECIES = 6; // 리자몽
    private static readonly int[] PartySpecies = [150, 384, 448, 248, 94]; // 뮤츠, 레쿠쟈, 루카리오, 마기라스, 팬텀

    public static PokemonState Create(MegaDex megas)
    {
        var state = new PokemonState
        {
            SpeciesId = LEADER_SPECIES,
            AgeMinutes = MAX_LEVEL_MINUTES,
            AgeSeconds = MAX_LEVEL_MINUTES * 60,
            TrAtk = TRAINED_STAT,
            TrDef = TRAINED_STAT,
            TrSpeed = TRAINED_STAT,
            Fullness = 100,
            Joy = 100,
            Energy = 100,
            Hygiene = 100,
            Bond = 100,
            Genes = MaxGenes(),
            GymBadges = 8,
            HasMegaRing = true,
            MegaStones = megas.CollectibleStones().Select(stone => stone.StoneId).ToList(),
            Items = ItemDex.All.ToDictionary(item => item.Id, _ => ITEM_COUNT),
            Settings = new GameSettings { FarewellAgeDays = 365, RoamingPokemonCount = 6 },
            UnlockedPokemon = [LEADER_SPECIES, .. PartySpecies],
        };

        foreach (int species in PartySpecies)
        {
            state.Party.Add(new PartyMember
            {
                SpeciesId = species,
                AgeMinutes = MAX_LEVEL_MINUTES,
                TrAtk = TRAINED_STAT,
                TrDef = TRAINED_STAT,
                TrSpeed = TRAINED_STAT,
                Fullness = 100,
                Joy = 100,
                Energy = 100,
                Hygiene = 100,
                Bond = 100,
                Genes = MaxGenes(),
            });
        }
        return state;
    }

    private static PokemonGene MaxGenes() => new() { HpGene = MAX_GENE, AtkGene = MAX_GENE, DefGene = MAX_GENE, SpeGene = MAX_GENE };
}
#endif
