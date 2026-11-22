using System;

public static class EventBus<T> where T : struct
{
    // 실행할 정적이벤트 키
    private static event Action<T> OnEvent;
    
    // 키에 이벤트를 등록/해제하는 메서드
    public static void Subscribe(Action<T> handler) => OnEvent += handler;
    public static void Unsubscribe(Action<T> handler) => OnEvent -= handler;
    // 중간다리에 넣는거
    public static void Raise(T eventData) => OnEvent?.Invoke(eventData);
}
