using System;

namespace CardGame.Core
{
    [Serializable]
    public sealed class RuleTraceEntry
    {
        public string Kind;
        public int PlayerId;
        public long SourceInstanceId;
        public string SourceCardId;
        public string SourceName;
        public long TargetInstanceId;
        public string TargetCardId;
        public string TargetName;
        public long IntendedTargetInstanceId;
        public string AbilityId;
        public string Timing;
        public string Operation;
        public string Detail;
        public int SourcePower;
        public int TargetPower;
        public string[] SourceKeywords = Array.Empty<string>();
        public string[] TargetKeywords = Array.Empty<string>();
        public bool SourceLost;
        public bool TargetLost;
        public long[] CandidateInstanceIds = Array.Empty<long>();
        public long[] SelectedInstanceIds = Array.Empty<long>();
        public string[] Options = Array.Empty<string>();
        public int ChoiceValue;
        public int Minimum;
        public int Maximum;
        public bool Automatic;
        public string FromZone;
        public string ToZone;
        public int CausedByPlayerId = -1;

        internal static RuleTraceEntry ForCard(string kind, int playerId,
            CardInstance source, CardInstance target = null) => new RuleTraceEntry
        {
            Kind = kind,
            PlayerId = playerId,
            SourceInstanceId = source?.InstanceId ?? 0,
            SourceCardId = source?.Definition.Id,
            SourceName = source?.Definition.Name,
            TargetInstanceId = target?.InstanceId ?? 0,
            TargetCardId = target?.Definition.Id,
            TargetName = target?.Definition.Name
        };

        internal static RuleTraceEntry ForAbility(string kind, int playerId,
            CardInstance source, RuleAbilityDefinition ability)
        {
            var entry = ForCard(kind, playerId, source);
            entry.AbilityId = ability.Id;
            entry.Timing = ability.Timing.ToString();
            entry.Operation = ability.Operation.ToString();
            return entry;
        }
    }
}
