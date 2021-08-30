namespace WolvenKit.RED4.Types
{
    public class CWeakHandle<T> : IRedHandle<T> where T : IRedClass
    {
        public int Pointer { get; set; }

        public int GetValue() => Pointer;
        public void SetValue(int value) => Pointer = value;
    }
}
