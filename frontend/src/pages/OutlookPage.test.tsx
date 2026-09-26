import { screen, within } from '@testing-library/react';

import type { CardPrediction, ModelSummary, PredictionList } from '../api/types';
import { mockApi, renderRoute } from '../test-utils';
import OutlookPage from './OutlookPage';

const riser: CardPrediction = {
  cardId: 'sv3pt5-25',
  name: 'Pikachu',
  number: '25',
  setId: 'sv3pt5',
  setName: '151',
  imageUrl: null,
  tcgplayerUrl: null,
  variant: 'holofoil',
  variantLabel: 'Holofoil',
  current: 10,
  predicted: 12.5,
  low: 10.8,
  high: 14.2,
  changePercent: 25,
  reasons: [{ feature: 'pokemon_premium', text: 'Pikachu cards sell for 3.1× the typical card', effectPercent: 9.4 }],
};

const published = (cards: CardPrediction[]): PredictionList => ({ status: 'Published', asOf: '2026-09-24', horizonDays: 30, cards });

const model: ModelSummary = {
  status: 'Published',
  asOf: '2026-09-24',
  trainedAt: '2026-09-24T11:46:00Z',
  horizonDays: 30,
  trainingRows: 120000,
  validationRows: 30000,
  typicalErrorPercent: 11.2,
  noChangeErrorPercent: 14.8,
  directionAccuracyPercent: 63.5,
  rangeLowPercent: -12,
  rangeHighPercent: 15,
  importance: [
    { feature: 'change_30d', label: 'Change over 30 days', weight: 21.3 },
    { feature: 'pokemon_premium', label: 'Pokémon popularity', weight: 14.9 },
  ],
  trackRecord: [{ asOf: '2026-08-20', horizonDays: 30, count: 9800, typicalErrorPercent: 10.9, noChangeErrorPercent: 13.1, directionAccuracyPercent: 61 }],
  message: null,
};

describe('OutlookPage', () => {
  it('lists predicted risers with their top reason, and shows how the model has done', async () => {
    mockApi({
      '/api/predictions?direction=up&limit=12': published([riser]),
      '/api/predictions?direction=down&limit=12': published([]),
      '/api/predictions/model': model,
    });
    renderRoute('/outlook', '/outlook', <OutlookPage />);

    const rise = await screen.findByRole('region', { name: 'Predicted to rise' });
    const link = await within(rise).findByRole('link', { name: /Pikachu/ });
    expect(link).toHaveAttribute('href', '/cards/sv3pt5-25');
    expect(link).toHaveTextContent('$12.50');
    expect(link).toHaveTextContent('+25.0%');
    expect(link).toHaveTextContent('Pikachu cards sell for 3.1× the typical card');
    expect(await within(screen.getByRole('region', { name: 'Predicted to fall' })).findByText('No predictions in this direction right now.')).toBeInTheDocument();

    expect(await screen.findByText('11.2%')).toBeInTheDocument();
    expect(screen.getByText('14.8%')).toBeInTheDocument();
    expect(screen.getByText('Change over 30 days')).toBeInTheDocument();
    expect(screen.getByRole('table')).toHaveTextContent('9,800');
    expect(screen.getByText(/not financial advice/)).toBeInTheDocument();
  });

  it('explains why there are no predictions yet', async () => {
    const waiting: PredictionList = { status: 'InsufficientHistory', asOf: '2026-09-24', horizonDays: 30, cards: [] };
    mockApi({
      '/api/predictions?direction=up&limit=12': waiting,
      '/api/predictions?direction=down&limit=12': waiting,
      '/api/predictions/model': {
        ...model,
        status: 'InsufficientHistory',
        typicalErrorPercent: null,
        importance: [],
        trackRecord: [],
        message: 'Needs about 74 days of prices to train and test a 30-day model; 12 recorded so far.',
      },
    });
    renderRoute('/outlook', '/outlook', <OutlookPage />);

    expect(await screen.findByText(/Needs about 74 days of prices/)).toBeInTheDocument();
    expect(await screen.findAllByText(/Predictions start once there is enough price history/)).toHaveLength(2);
  });
});
