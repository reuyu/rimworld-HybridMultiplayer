using System;

namespace HybridShared
{
    /// <summary>
    /// 스케줄된 액션 - 특정 틱에 실행될 플레이어 명령.
    /// Multiplayer 모드의 ScheduledCommand를 참조하여 간소화.
    /// </summary>
    public class ScheduledAction
    {
        /// <summary>액션 타입</summary>
        public ActionType Type { get; set; }
        
        /// <summary>실행 예정 틱</summary>
        public int ExecuteTick { get; set; }
        
        /// <summary>발신 플레이어 ID</summary>
        public int PlayerId { get; set; }
        
        /// <summary>대상 Thing ID (옵션)</summary>
        public int? TargetThingId { get; set; }
        
        /// <summary>대상 위치 [x, y, z] (옵션)</summary>
        public float[] TargetPosition { get; set; }
        
        /// <summary>추가 데이터 (옵션)</summary>
        public byte[] ExtraData { get; set; }
        
        /// <summary>생성 시간 (디버그용)</summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        public ScheduledAction() { }
        
        public ScheduledAction(ActionType type, int executeTick, int playerId)
        {
            Type = type;
            ExecuteTick = executeTick;
            PlayerId = playerId;
        }
        
        /// <summary>디버그용 문자열</summary>
        public override string ToString()
        {
            string target = TargetThingId.HasValue ? $"ThingID:{TargetThingId}" : "";
            string pos = TargetPosition != null ? $"Pos:({TargetPosition[0]:F1},{TargetPosition[2]:F1})" : "";
            string extra = !string.IsNullOrEmpty(target) || !string.IsNullOrEmpty(pos) 
                ? $" [{target}{(string.IsNullOrEmpty(target) ? "" : ", ")}{pos}]" 
                : "";
            return $"{Type}@Tick{ExecuteTick} (Player:{PlayerId}){extra}";
        }
    }
    
    /// <summary>
    /// 액션 타입 정의.
    /// 전투 중 플레이어가 수행할 수 있는 명령들.
    /// </summary>
    public enum ActionType : byte
    {
        /// <summary>없음</summary>
        None = 0,
        
        /// <summary>징집/해제</summary>
        Draft = 1,
        
        /// <summary>이동 명령</summary>
        Move = 2,
        
        /// <summary>원거리 공격</summary>
        Attack = 3,
        
        /// <summary>근접 공격</summary>
        AttackMelee = 4,
        
        /// <summary>능력 사용</summary>
        UseAbility = 5,
        
        /// <summary>아이템 사용</summary>
        UseItem = 6,
        
        /// <summary>현재 작업 취소</summary>
        CancelJob = 7,
        
        /// <summary>장비 장착</summary>
        Equip = 8,
        
        /// <summary>아이템 버리기</summary>
        Drop = 9,
        
        /// <summary>대기 (Hold Position)</summary>
        Hold = 10,
        
        /// <summary>사격 중지</summary>
        HoldFire = 11,
    }
}
