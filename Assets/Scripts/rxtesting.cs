using System;
using UniRx;
using UnityEngine;

namespace Assets.Scripts
{
    public class rxtesting : MonoBehaviour
    {
        private IObservable<string> _stream;

        private IDisposable _subscription;

        // Start is called before the first frame update
        void Start()
        {
            _stream = Observable.Create<string>((obs) =>
            {
                var sub = Observable.EveryUpdate().Where(fc => fc % 2 == 0).Subscribe(dt => { obs.OnNext(dt.ToString()); });

                return Disposable.Create(() =>
                {
                    sub.Dispose();
                    Debug.Log("Observer unsubscribed!");
                });
            });

            _subscription = _stream.Subscribe(Debug.Log);
        }

        public IObservable<string> MessageStream() => _stream;

        // Update is called once per frame
        void Update()
        {
            if (Input.GetMouseButtonDown(0))
            {
                _subscription.Dispose();
            }
        }
    }
}
