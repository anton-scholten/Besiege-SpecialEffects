namespace SpecialEffectsMod
{
    // The strobe the Spot Light and the Glass block both run: a string of
    // characters walked one every Interval seconds, with the frames in between
    // blending towards the next character.
    //
    // This owns the walking only. What a character means -- a brightness and a
    // cone angle for one block, a colour and an alpha for the other -- is the
    // block's own business, and the two do not agree on it.
    public class Strobe
    {
        private int index;
        private int counter;
        private bool rollOver = true;

        // The characters being blended between, valid once Step has restarted.
        public char From;
        public char To;

        public void Reset()
        {
            index = 0;
            counter = 0;
            rollOver = true;
            From = '-';
            To = '-';
        }

        // False on the frame the sequence rolls over to the next character, when
        // there is nothing to draw. Otherwise `blend` runs from From to To, and
        // `restart` says the pair has just changed and anything read off them is
        // stale.
        public bool Step(string sequence, float framesPerStep, out bool restart, out float blend)
        {
            restart = false;
            blend = 0f;

            if (rollOver)
            {
                rollOver = false;
                if (index >= sequence.Length) index = 0;
                From = sequence[index];
                int next = index + 1;
                To = sequence[next == sequence.Length ? 0 : next];
                index++;
                restart = true;
                return true;
            }

            if (counter >= framesPerStep)
            {
                counter = 0;
                rollOver = true;
                return false;
            }

            counter++;
            blend = counter / framesPerStep;
            return true;
        }
    }
}
