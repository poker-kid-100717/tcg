import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

import { mockApi, renderRoute } from '../test-utils';
import UnsubscribePage from './UnsubscribePage';

describe('UnsubscribePage', () => {
  it('turns alert emails off only after a click', async () => {
    const fetchMock = mockApi({ '/api/email/unsubscribe': {} });
    const user = userEvent.setup();
    renderRoute('/email/unsubscribe?token=abc', '/email/unsubscribe', <UnsubscribePage />);

    expect(fetchMock).not.toHaveBeenCalled();
    await user.click(screen.getByRole('button', { name: 'Stop alert emails' }));

    expect(await screen.findByText(/You won't get watchlist alert emails any more/)).toBeInTheDocument();
    const [url, init] = fetchMock.mock.calls[0];
    expect(String(url)).toBe('/api/email/unsubscribe?token=abc');
    expect(init?.method).toBe('POST');
  });
});
