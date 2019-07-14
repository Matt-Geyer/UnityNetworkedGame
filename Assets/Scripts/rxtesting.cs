using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UniRx;
using UniRx.InternalUtil;
using UnityEngine;

namespace Assets.Scripts
{
    public class rxtesting : MonoBehaviour
    {
        private IObservable<Testin> _stream;

        private IDisposable _subscription;

        private class Subcription
        {
            private readonly IObserver<Testin> _observer;

            public Subcription(IObserver<Testin> observer)
            {
                _observer = observer;
            }

            public void OnMessage(Testin msg)
            {
                _observer.OnNext(msg);
            }

        }

        private class Testin
        {
            public readonly Guid Id = Guid.NewGuid();

            public int Data;

            public void LongBlockingTask(int ms)
            {
                Task.Delay(ms).Wait();
            }
        }

        private delegate void HandleMessage(Testin msg);

        private event HandleMessage NextMessage;



        

        // Start is called before the first frame update
        void Start()
        {

            Observable.Timer(DateTimeOffset.Now, TimeSpan.FromMilliseconds(10)).Subscribe(_ => Debug.Log("TIMER TICKIN"));

            //Queue<Testin> messages = new Queue<Testin>();

            //for (int i = 0; i < 100; i++)
            //    messages.Enqueue(new Testin { Data = (i + 1) * 10});


            //IConnectableObservable<Testin> testinStream = Observable.Create<Testin>(
            //    observer =>
            //    {

            //        IDisposable sub = Observable.EveryFixedUpdate().Subscribe(_ =>
            //        {
            //            if (messages.Count > 0)
            //                observer.OnNext(messages.Dequeue());
            //        });

            //        return Disposable.Create(() => { sub.Dispose(); });
            //    }).Publish();

            //IObservable<Testin> publicStream = testinStream.RefCount();




            //Observable.EveryFixedUpdate().SampleFrame(10).Subscribe(_ =>
            //{
            //    if (messages.Count > 0)
            //    {
            //        NextMessage?.Invoke(messages.Dequeue());
            //    }
            //});

            //List<Testin> messagesEvent = new List<Testin>();
            //var msgStream = Observable.EveryFixedUpdate().SampleFrame(10).Select(_ =>
            //{
            //    if (messages.Count > 0)
            //    {
            //        return messages.Dequeue();
            //    }

            //    return null;
            //}).Publish();

            //var testStrm = msgStream.RefCount();



            //List<IObserver<List<Testin>>> disBad = new List<IObserver<List<Testin>>>();
            //List<Testin> messagesEvent = new List<Testin>();
            //var msgStream = Observable.Create<List<Testin>>(obs =>
            //{
            //    disBad.Add(obs);
            //    return Disposable.Create(() => { disBad.Remove(obs); });
            //});

            //Observable.EveryFixedUpdate().SampleFrame(100).Subscribe(_ =>
            //{
            //    messagesEvent.Clear();
            //    for (int i = messages.Count; i > 0; --i)
            //    {
            //        messagesEvent.Add(messages.Dequeue());
            //    }

            //    foreach (var obs in disBad)
            //        obs.OnNext(messagesEvent);
            //});


            //_stream = Observable.Create<Testin>((obs) =>
            //{
            //    Subcription sub = new Subcription(obs);

            //    NextMessage += sub.OnMessage;

            //    return Disposable.Create(() =>
            //    {
            //        NextMessage -= sub.OnMessage;
            //        Debug.Log("Observer unsubscribed!");
            //    });
            //});



            //_subscription = publicStream
            //    .Do(msg => { Debug.Log($"OBSERVER1 {msg.Id} - {msg.Data}"); })
            //    .Subscribe(msg => { msg.Data -= 2; });

            //publicStream
            //    .Do(msg => { Debug.Log($"OBSERVER2 {msg.Id} - {msg.Data}"); })
            //    .Subscribe(msgs =>
            //{
            //        Debug.Log($"OBSERVER2 {msgs.Id} - {msgs.Data}");
            //});



            // testinStream.Connect();
        }


        // Update is called once per frame
        void Update()
        {

            Debug.Log("STARTED UPDATE WAIT");
            //Task.Delay(1000).Wait();
            //Debug.Log("STOPPED WAIT");

            //if (Input.GetMouseButtonDown(0))
            //{
            //    _subscription.Dispose();
            //}
        }
    }
}
