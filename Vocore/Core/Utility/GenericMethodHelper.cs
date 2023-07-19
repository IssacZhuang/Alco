using System;

namespace Vocore
{
    public class GenericMethodHelper<TParam>
    {
        public delegate void GenericMethodDelegate(TParam param);

        private GenericMethodDelegate _method;
        private object _target;

        public GenericMethodHelper(GenericMethodDelegate method)
        {
            _method = method;
            if (method.Target != null)
            {
                _target = method.Target;
            }
        }

        
    }
}