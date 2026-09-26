import { useMemo, useState } from 'react';
import { Link } from 'react-router-dom';

const questions = [
  'Which feature would actually help Pokémon collectors you see in your store?',
  'What would make you distrust the pricing or market data?',
  'Would collectors use the Master Set tracker to identify missing cards?',
  'How valuable is knowing where sealed Pokémon inventory is available locally?',
  'Would you want your store inventory included if TCG Signal could send nearby collectors to you?',
  'What information should TCG Signal show about a participating local card store?',
  'What is the most obvious feature this product is missing?',
];

export default function FeedbackPage() {
  const [answers, setAnswers] = useState(() => questions.map(() => ''));
  const [copied, setCopied] = useState(false);

  const summary = useMemo(
    () =>
      [
        'TCG Signal — local store feedback',
        '',
        ...questions.flatMap((question, index) => [
          question,
          answers[index].trim() || '(no answer)',
          '',
        ]),
      ].join('\n'),
    [answers],
  );

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(summary);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2500);
    } catch {
      setCopied(false);
    }
  };

  return (
    <div className="container-custom grid gap-8 py-10 sm:py-14">
      <header className="mx-auto grid max-w-3xl gap-3 text-center">
        <p className="eyebrow">Early partner preview</p>
        <h1 className="text-4xl sm:text-5xl">I want the useful criticism.</h1>
        <p className="text-lg leading-8 text-slate-600">
          This build is being shared for feedback, not to sell anything. Please evaluate it like someone who talks
          with Pokémon collectors every day: what solves a real problem, what feels unnecessary, and what would need
          to change before you would trust or recommend it?
        </p>
      </header>

      <section className="panel mx-auto grid w-full max-w-4xl gap-6 p-6 sm:p-8">
        {questions.map((question, index) => (
          <label key={question} className="grid gap-2">
            <span className="font-semibold text-slate-900">{index + 1}. {question}</span>
            <textarea
              value={answers[index]}
              onChange={(event) =>
                setAnswers((current) => current.map((value, i) => (i === index ? event.target.value : value)))
              }
              rows={3}
              className="w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm leading-6 text-slate-900 focus:border-pokemon-blue focus:ring-2 focus:ring-pokemon-blue/20 focus:outline-none"
              placeholder="Your feedback…"
            />
          </label>
        ))}

        <div className="flex flex-wrap items-center gap-3 border-t border-slate-100 pt-5">
          <button type="button" onClick={copy} className="btn bg-pokemon-pokeblue text-white">
            {copied ? 'Copied — thank you' : 'Copy feedback'}
          </button>
          <p className="text-sm text-slate-500">
            Copy the responses and reply to the person who sent you this preview link.
          </p>
        </div>
      </section>

      <section className="mx-auto grid max-w-4xl gap-4 md:grid-cols-2">
        <div className="panel p-6">
          <p className="eyebrow">Why local stores matter</p>
          <h2 className="mt-1 text-xl">The inventory feature should help local shops compete for local demand.</h2>
          <p className="mt-2 text-sm leading-6 text-slate-600">
            One direction being tested is verified inventory supplied by participating card stores, with nearby
            collectors sent directly to the store instead of treating national retailers as the only source.
          </p>
        </div>
        <div className="panel p-6">
          <p className="eyebrow">Nothing fake</p>
          <h2 className="mt-1 text-xl">Missing data is shown as missing.</h2>
          <p className="mt-2 text-sm leading-6 text-slate-600">
            The product intentionally avoids invented inventory quantities, sold comps, or market confidence. If a
            provider cannot verify something, the preview should say so.
          </p>
        </div>
      </section>

      <div className="text-center">
        <Link to="/" className="font-semibold text-pokemon-pokeblue hover:underline">← Back to TCG Signal</Link>
      </div>
    </div>
  );
}
