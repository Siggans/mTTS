
namespace mTTS.Utilities
{
    using System;
    using System.Diagnostics;
    using System.Text;
    using System.Threading.Tasks;

    public class BlockingTextBuffer
    {
        private object m_lock = new Object();
        private readonly string[] m_array;

        private int m_count = 0;

        private int m_end = -1;
        public int BufferSize { get; private set; } = 0;
        public int Length => this.m_count;


        public BlockingTextBuffer( int size )
        {
            if ( size < 0 )
            {
                throw new ArgumentOutOfRangeException( nameof( size ), "Cannot be less than zero" );
            }
            this.BufferSize = size;
            this.m_array = new string[size];
        }

        public async Task InsertAsync( string message )
        {
            if ( message == null ) { throw new ArgumentNullException( nameof( message ) ); }
            if ( this.BufferSize == 0 ) { return; }

            if ( Util.IsUiThread ) { await Task.Run( () => this.Insert( message ) ); }
            // Non UI thread, let's do our blocking action there.
            else { this.Insert( message ); }
        }

        public async Task<string> GetOutputAsync()
        {
            if ( this.BufferSize == 0 || this.Length == 0 ) { return ""; }
            if ( Util.IsUiThread ) { return await Task.Run( () => this.GetOutput() ); }
            // Non UI thread, let's do our blocking action there.
            else { return this.GetOutput(); }
        }

        private void Insert( string message )
        {
            Debug.Assert( !Util.IsUiThread );
            // Condition BufferSize 0 should be checked before calling this function.
            lock ( this.m_lock )
            {
                this.m_end = (this.m_end + 1) % this.BufferSize;
                this.m_count = this.m_count > this.BufferSize ? this.BufferSize : this.m_count + 1;
                this.m_array[this.m_end] = message;
            }
        }

        private string GetOutput()
        {
            Debug.Assert( !Util.IsUiThread );
            // Condition BufferSize 0 and count 0 should be checked before calling this function.
            lock ( this.m_lock )
            {
                var sb = new StringBuilder();
                for ( var i = this.m_end - this.m_count + 1; i <= this.m_end; i++ )
                {
                    var index = (i+this.BufferSize) % this.BufferSize;
                    sb.AppendLine( this.m_array[index] );
                }
                return sb.ToString();
            }
        }

    }
}

