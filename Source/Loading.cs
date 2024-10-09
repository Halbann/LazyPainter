using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace LazyPainter
{
    public static class Loading
    {
        public static float minFrameTime = 1 / 128f;

        /// <summary>
        /// Reimplementation of StartCoroutine supporting nested yield StartCoroutine() calls patched with PatchStartCoroutineInCoroutine()
        /// and yielding null only after a fixed amount of time elapsed
        /// </summary>
        public static IEnumerator FrameUnlockedCoroutine(IEnumerator coroutine)
        {
            // From KSPCommunityFixes. MIT license.
            // https://github.com/KSPModdingLibs/KSPCommunityFixes

            float nextFrameTime = Time.realtimeSinceStartup + minFrameTime;

            Stack<IEnumerator> enumerators = new Stack<IEnumerator>();
            enumerators.Push(coroutine);

            while (enumerators.TryPop(out IEnumerator currentEnumerator))
            {
                bool moveNext;

                try
                {
                    moveNext = currentEnumerator.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    moveNext = false;
                }

                while (moveNext)
                {
                    if (Time.realtimeSinceStartup > nextFrameTime)
                    {
                        yield return null; // Moving this line above nextFrameTime turns minFrameTime into a max allowed time by our routine per frame.
                        nextFrameTime = Time.realtimeSinceStartup + minFrameTime;
                    }

                    if (currentEnumerator.Current is IEnumerator nestedCoroutine)
                    {
                        enumerators.Push(currentEnumerator);
                        currentEnumerator = nestedCoroutine;
                        continue;
                    }

                    try
                    {
                        moveNext = currentEnumerator.MoveNext();
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                        moveNext = false;
                    }
                }
            }
        }

        private static bool TryPop<T>(this Stack<T> stack, out T result)
        {
            int size = stack.Count - 1;
            if ((uint)size >= (uint)stack.Count)
            {
                result = default;
                return false;
            }

            result = stack.Pop();
            return true;
        }
    }
}
