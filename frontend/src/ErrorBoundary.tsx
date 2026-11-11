import { Component, type ErrorInfo, type ReactNode } from 'react';

interface Props {
  children: ReactNode;
}

interface State {
  hasError: boolean;
  errorMessage: string;
}

export class ErrorBoundary extends Component<Props, State> {
  public state: State = {
    hasError: false,
    errorMessage: ''
  };

  public static getDerivedStateFromError(error: Error): State {
    return {
      hasError: true,
      errorMessage: error.message || 'একটি অপ্রত্যাশিত ত্রুটি ঘটেছে।'
    };
  }

  public componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('UI ErrorBoundary caught error:', error, errorInfo);
  }

  public render() {
    if (this.state.hasError) {
      return (
        <div className="error-boundary-screen">
          <div className="error-boundary-card">
            <h2>পেজটি লোড করতে সমস্যা হয়েছে</h2>
            <p>
              অনুগ্রহ করে পেজটি রিলোড করুন। সমস্যাটি বারবার হলে সার্ভার সংযোগ যাচাই করুন।
            </p>
            {this.state.errorMessage && (
              <p className="error-boundary-detail">{this.state.errorMessage}</p>
            )}
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => window.location.reload()}
            >
              পেজ রিলোড করুন
            </button>
          </div>
        </div>
      );
    }

    return this.props.children;
  }
}

