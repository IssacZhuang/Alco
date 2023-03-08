namespace Vocore
{
    public class StaticHashObject
    {
        private int _hash = -1;
        public override int GetHashCode()
        {
            if (_hash < 0)
            {
                _hash = base.GetHashCode();
            }

            return _hash;
        }
    }
}


