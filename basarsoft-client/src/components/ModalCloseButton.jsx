const iconProps = {
  width: 18,
  height: 18,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  'aria-hidden': true,
}

export default function ModalCloseButton({ onClick, label = 'Close' }) {
  return (
    <button type="button" className="modal-close-btn" onClick={onClick} aria-label={label}>
      <svg {...iconProps}>
        <path d="M6 6l12 12M18 6L6 18" />
      </svg>
    </button>
  )
}
