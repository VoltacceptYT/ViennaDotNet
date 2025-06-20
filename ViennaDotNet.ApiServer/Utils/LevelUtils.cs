using ViennaDotNet.DB;
using ViennaDotNet.DB.Models.Player;
using ViennaDotNet.StaticData;
using static ViennaDotNet.DB.Models.Player.Tokens;

namespace ViennaDotNet.ApiServer.Utils;

public sealed class LevelUtils
{
    public static EarthDB.Query CheckAndHandlePlayerLevelUp(string playerId, long currentTime, StaticData.StaticData staticData)
    {
        EarthDB.Query getQuery = new EarthDB.Query(true);
        getQuery.Get("profile", playerId, typeof(Profile));
        getQuery.Then(results =>
        {
            Profile profile = (Profile)results.Get("profile").Value;
            EarthDB.Query updateQuery = new EarthDB.Query(true);
            bool changed = false;
            while (profile.level - 1 < staticData.levels.levels.Length && profile.experience >= staticData.levels.levels[profile.level - 1].experienceRequired)
            {
                changed = true;
                profile.level++;
                Rewards rewards = MakeLevelRewards(staticData.levels.levels[profile.level - 2]);
                updateQuery.Then(TokenUtils.AddToken(playerId, new LevelUpToken(profile.level, rewards.ToDBRewardsModel())), false);
            }

            if (changed)
                updateQuery.Update("profile", playerId, profile);

            return updateQuery;
        });

        return getQuery;
    }

    public static Rewards MakeLevelRewards(Levels.Level level)
    {
        Rewards rewards = new Rewards();
        if (level.rubies > 0)
        {
            rewards.addRubies(level.rubies);
        }

        foreach (var item in level.items)
        {
            rewards.addItem(item.id, item.count);
        }

        foreach (string buildplate in level.buildplates)
        {
            rewards.addBuildplate(buildplate);
        }

        return rewards;
    }
}
